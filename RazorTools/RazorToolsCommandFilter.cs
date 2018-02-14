using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OLEConstants = Microsoft.VisualStudio.OLE.Interop.Constants;

namespace RazorTools
{
    internal sealed class RazorToolsCommandFilter : IOleCommandTarget
    {
        private const string CODE_BEHIND_FILE_SUFFIX = ".cshtml.cs";
        private const string RAZOR_PAGES_FILE_SUFFIX = ".cshtml";

        private readonly IWpfTextView textView;
        private readonly IWpfTextViewHost textViewHost;
        private readonly IClassifier classifier;
        private readonly SVsServiceProvider globalServiceProvider;
        private IEditorOperations editorOperations;

        public RazorToolsCommandFilter(IWpfTextView textView, IClassifierAggregatorService aggregatorFactory,
            SVsServiceProvider globalServiceProvider, IEditorOperationsFactoryService editorOperationsFactory, IWpfTextViewHost textViewHost)
        {
            this.textView = textView;
            classifier = aggregatorFactory.GetClassifier(textView.TextBuffer);
            this.globalServiceProvider = globalServiceProvider;
            editorOperations = editorOperationsFactory.GetEditorOperations(textView);
            this.textViewHost = textViewHost;
        }

        public IOleCommandTarget Next { get; internal set; }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            // Command handling registration
            if (pguidCmdGroup == Constants.RazorToolsGuid && cCmds == 1)
            {
                switch (prgCmds[0].cmdID)
                {
                    case Constants.ToggleCodeBehindViewCmdId:
                        return HandleQueryStatus_ToggleCodeBehindView(prgCmds, pCmdText);
                }
            }

            if (Next != null)
            {
                return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
            return (int)OLEConstants.OLECMDERR_E_UNKNOWNGROUP;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            // Command handling
            if (pguidCmdGroup == Constants.RazorToolsGuid)
            {
                // Dispatch to the correct command handler
                switch (nCmdID)
                {
                    case Constants.ToggleCodeBehindViewCmdId:
                        return HandleExec_ToggleCodeBehindView();
                }
            }

            // No commands called. Pass to next command handler.
            if (Next != null)
            {
                return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
            return (int)OLEConstants.OLECMDERR_E_UNKNOWNGROUP;
        }

        //---------------- Specific handlers -----------------

        private int HandleQueryStatus_ToggleCodeBehindView(OLECMD[] prgCmds, IntPtr pCmdText)
        {
            // Command should be enabled if the file is *.cshtml with associated *.cshtml.cs (or vice-versa)
            string currentFilePath = GetCurrentFilePath();
            if (HasAssociatedRazorFile(currentFilePath))
            {
                prgCmds[0].cmdf |= (uint)OLECMDF.OLECMDF_SUPPORTED;
                prgCmds[0].cmdf |= (uint)OLECMDF.OLECMDF_ENABLED;

                // Update the menu item text - if QueryStatus is asking for potential text changes
                if (pCmdText != IntPtr.Zero)
                {
                    UpdateMenuItemText(pCmdText, currentFilePath);
                }
            }
            return VSConstants.S_OK;
        }

        private static void UpdateMenuItemText(IntPtr pCmdText, string currentFilePath)
        {
            // Note: This was causing errors
            string newText = IsFileType(currentFilePath, RAZOR_PAGES_FILE_SUFFIX) 
                ? "Code &Behind" : "&Razor Page";
            VsShellUtilities.SetOleCmdText(pCmdText, newText);
            //Microsoft.VisualStudio.PlatformUI.OleCommandUtilities.SetOleCmdText(pCmdText, newText);
            //OLECMDTEXT.SetText(pCmdText, newText);
        }

        private int HandleExec_ToggleCodeBehindView()
        {
            string relatedFilePath = GetRelatedFilePath();
            if (relatedFilePath == null)
            {
                MessageBox.Show("No matching Razor/CodeBehind file.");
                return VSConstants.S_OK;
            }

            System.Diagnostics.Debug.WriteLine($"Opening related Razor file: {relatedFilePath}");
            //OpenFileViaDTE(relatedFilePath);
            //OpenFileViaCommandDispatcher(relatedFilePath);
            OpenFileViaVsShellUtilities(relatedFilePath);

            return VSConstants.S_OK;
        }

        private void OpenFileViaVsShellUtilities(string filePath)
        {
            VsShellUtilities.OpenDocument(globalServiceProvider, filePath);
        }

        private void OpenFileViaDTE(string filePath)
        {
            DTE dte = (DTE)globalServiceProvider.GetService(typeof(DTE));
            dte.ExecuteCommand("File.OpenFile", filePath);
        }

        private void OpenFileViaCommandDispatcher(string filePath)
        {
            Guid cmdGroup = VSConstants.VSStd2K;
            uint cmdID = (uint)VSConstants.VSStd2KCmdID.OPENFILE;

            string inArg = filePath;
            IntPtr inArgPtr = Marshal.AllocCoTaskMem(200);   // TODO: Calculate sizeof string
            Marshal.GetNativeVariantForObject(filePath, inArgPtr);

            IOleCommandTarget commandDispatcher = GetShellCommandDispatcher();
            commandDispatcher.Exec(ref cmdGroup, cmdID, (uint)OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT, inArgPtr, IntPtr.Zero);
            Marshal.FreeCoTaskMem(inArgPtr);
        }

        private IOleCommandTarget GetShellCommandDispatcher()
        {
            return globalServiceProvider.GetService(typeof(SUIHostCommandDispatcher)) as IOleCommandTarget;
        }

        private bool HasAssociatedRazorFile(string filePath)
        {
            string relatedFilePath = GetRelatedFilePath(filePath);
            return relatedFilePath != null && File.Exists(relatedFilePath);
        }

        private string GetRelatedFilePath(string filePath = null)
        {
            filePath = filePath ?? GetCurrentFilePath();
            if (filePath == null)
            {
                // Error state
                System.Diagnostics.Debug.WriteLine("Unable to get current file path.");
                return null;
            }

            string relatedFileName = null;
            if (IsFileType(filePath, RAZOR_PAGES_FILE_SUFFIX))
            {
                // Is there a matching CodeBehind file?
                string pathWithoutExtension = filePath.Substring(0, filePath.LastIndexOf(RAZOR_PAGES_FILE_SUFFIX));
                relatedFileName = pathWithoutExtension + CODE_BEHIND_FILE_SUFFIX;
            }
            else if (IsFileType(filePath, CODE_BEHIND_FILE_SUFFIX))
            {
                // Is there a matching Razor file?
                string pathWithoutExtension = filePath.Substring(0, filePath.LastIndexOf(CODE_BEHIND_FILE_SUFFIX));
                relatedFileName = pathWithoutExtension + RAZOR_PAGES_FILE_SUFFIX;
            }
            return relatedFileName;
        }

        private string GetCurrentFilePath()
        {
            textViewHost.TextView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document);
            return document.FilePath;
        }

        private static bool IsFileType(string filePath, string fileSuffix)
        {
            return filePath.EndsWith(fileSuffix);
        }

    }
}
