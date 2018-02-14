using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
#pragma warning disable 0649

namespace RazorTools
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class RazorToolsTextViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        private IVsEditorAdaptersFactoryService EditorAdaptersFactoryService { get; set; }

        [Import]
        private SVsServiceProvider GlobalServiceProvider;

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            IWpfTextViewHost textViewHost = EditorAdaptersFactoryService.GetWpfTextViewHost(textViewAdapter);

            RazorToolsCommandFilter commandFilter = new RazorToolsCommandFilter(GlobalServiceProvider, textViewHost);
            textViewAdapter.AddCommandFilter(commandFilter, out IOleCommandTarget next);

            commandFilter.Next = next;
        }
    }
}
