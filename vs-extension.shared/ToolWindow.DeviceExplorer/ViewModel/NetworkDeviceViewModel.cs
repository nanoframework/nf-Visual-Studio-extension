//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// Data context of the dialog that asks for the token of a network device that requires one, or rejected the one it has.
    /// </summary>
    [DataContract]
    internal sealed class NetworkDeviceViewModel : NotifyPropertyChangedObject
    {
        private readonly CancellationTokenSource _closeDialog = new CancellationTokenSource();

        private string _token = string.Empty;
        private bool _isTokenVisible;

        private NetworkDeviceViewModel(string dialogTitle, string introduction)
        {
            DialogTitle = dialogTitle;
            Introduction = introduction;

            OkCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                Accepted = true;

                _closeDialog.Cancel();

                return Task.CompletedTask;
            })
            {
                CanExecute = Validate(),
            };

            CancelCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                _closeDialog.Cancel();

                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// A dialog that asks for the token of a device.
        /// </summary>
        public static NetworkDeviceViewModel ForToken(string deviceDescription, bool wasRejected) => new NetworkDeviceViewModel(
            "Authentication token",
            wasRejected
                ? $"{deviceDescription} rejected the authentication token. Enter the right one to connect."
                : $"{deviceDescription} requires an authentication token. Enter it to connect.");

        /// <summary>
        /// Cancelled to close the dialog, it's the cancellation token of the dialog.
        /// </summary>
        public CancellationToken CloseToken => _closeDialog.Token;

        /// <summary>
        /// The dialog was closed with OK.
        /// </summary>
        public bool Accepted { get; private set; }

        public string ResultToken => _token;

        [DataMember]
        public string DialogTitle { get; }

        [DataMember]
        public string Introduction { get; }

        [DataMember]
        public string Token
        {
            get => _token;
            set
            {
                if (SetProperty(ref _token, value ?? string.Empty))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(MaskedToken));

                    Validate();
                }
            }
        }

        /// <summary>
        /// One bullet for each character of the token, shown over the token box while it's hidden.
        /// </summary>
        [DataMember]
        public string MaskedToken => new string('•', _token.Length);

        [DataMember]
        public bool IsTokenVisible
        {
            get => _isTokenVisible;
            set
            {
                if (SetProperty(ref _isTokenVisible, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(IsTokenHidden));
                }
            }
        }

        [DataMember]
        public bool IsTokenHidden => !_isTokenVisible;

        [DataMember]
        public string TokenError { get; private set; }

        [DataMember]
        public AsyncCommand OkCommand { get; }

        [DataMember]
        public AsyncCommand CancelCommand { get; }

        // checks the values, returns true if they can be used
        private bool Validate()
        {
            string tokenError = NetworkDeviceTokens.Validate(_token);

            if (tokenError is null
                && _token.Length == 0)
            {
                // nothing to show for it, the button is disabled
                tokenError = string.Empty;
            }

            SetError(nameof(TokenError), ref tokenError);

            bool valid = tokenError is null;

            if (OkCommand != null)
            {
                OkCommand.CanExecute = valid;
            }

            return valid;
        }

        private void SetError(string propertyName, ref string error)
        {
            string shown = string.IsNullOrEmpty(error) ? null : error;

            switch (propertyName)
            {
                case nameof(TokenError):
                    TokenError = shown;
                    break;
            }

            RaiseNotifyPropertyChangedEvent(propertyName);
        }
    }
}
