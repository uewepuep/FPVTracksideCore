using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition
{
    public static class Popuper
    {
        public static IPopupProvider PopupProvider { get; set; }

        public static void PopupConfirmation(string question, Action onOk = null)
        {
            if (PopupProvider == null)
                return;

            PopupProvider.PopupConfirmation(question, onOk);
        }

        public static void PopupMessage(string message, Action onOk = null)
        {
            if (PopupProvider == null)
                return;

            PopupProvider.PopupMessage(message, onOk);
        }

        public static void PopupCombinedMessage(string message, Action onOk = null)
        {
            if (PopupProvider == null)
                return;

            PopupProvider.PopupCombinedMessage(message, onOk);
        }

        public static void PopupError(string message, Exception exception, Action onOk = null)
        {
            if (PopupProvider == null)
                return;

            PopupProvider.PopupError(message, exception, onOk);
        }
    }

    public interface IPopupProvider
    {
        void PopupConfirmation(string question, Action onOk = null);

        void PopupMessage(string message, Action onOk = null);

        public void PopupCombinedMessage(string message, Action onOk = null);

        public void PopupError(string message, Exception exception, Action onOk = null);
    }
}
