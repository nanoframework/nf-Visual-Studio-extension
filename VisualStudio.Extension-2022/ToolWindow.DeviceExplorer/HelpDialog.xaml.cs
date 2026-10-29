//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Navigation;
    using Microsoft.VisualStudio.Imaging;
    using Microsoft.VisualStudio.Imaging.Interop;
    using Microsoft.VisualStudio.PlatformUI;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// The links to the documentation and the community, and the version of the extension.
    /// </summary>
    public partial class HelpDialog : DialogWindow
    {
        private static readonly IReadOnlyList<HelpLink> s_links = new List<HelpLink>
        {
            new HelpLink(KnownMonikers.Library, "Documentation", "Getting started guides, tutorials and how-tos.", "Read the docs", "https://docs.nanoframework.net/index.html"),
            new HelpLink(KnownMonikers.ClassLibrary, "API reference", "The classes and members of the class libraries.", "Browse the API", "https://docs.nanoframework.net/api/index.html"),
            new HelpLink(KnownMonikers.Code, "Samples", "Ready to run samples for most features and devices.", "Explore samples", "https://github.com/nanoframework/samples"),
            new HelpLink(KnownMonikers.Team, "Discord", "Ask questions and chat with the community and the team.", "Join us on Discord", "https://discord.gg/gCyBu8T"),
            new HelpLink(KnownMonikers.Bug, "Issues", "Found a bug or have an idea? Let us know.", "Report an issue", "https://github.com/nanoframework/Home/issues"),
            new HelpLink(KnownMonikers.GitHub, "GitHub", "Like the project? A star helps others find it.", "Star the project", "https://github.com/nanoframework/Home"),
            new HelpLink(KnownMonikers.Rating, "Marketplace", "Enjoying the extension? A review makes a difference.", "Rate the extension", "https://marketplace.visualstudio.com/items?itemName=nanoframework.nanoFramework-VS2022-Extension"),
            new HelpLink(KnownMonikers.Web, "Website", "News and everything about .NET nanoFramework.", "Visit the website", "https://www.nanoframework.net"),
        };

        private readonly DialogTelemetry _telemetry;

        /// <summary>
        /// Initializes a new instance of the <see cref="HelpDialog"/> class.
        /// </summary>
        public HelpDialog()
        {
            InitializeComponent();
            DialogIcon.Apply(this);

            _telemetry = DialogTelemetry.Start(this, "help");

            Links.ItemsSource = s_links;
            VersionText.Text = $"Version {NanoFrameworkPackage.NanoFrameworkExtensionVersion}";
        }

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            VsShellUtilities.OpenSystemBrowser(e.Uri.AbsoluteUri);

            ExtensionTelemetry.TrackHyperlinkClicked(e.Uri, "helpDialog");

            // the links that were opened, for the event recorded when the dialog is closed
            _telemetry.AddAction(ExtensionTelemetry.GetLinkName(e.Uri));

            e.Handled = true;
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// A link of the dialog.
        /// </summary>
        public sealed class HelpLink
        {
            public HelpLink(ImageMoniker moniker, string title, string description, string action, string url)
            {
                Moniker = moniker;
                Title = title;
                Description = description;
                Action = action;
                Url = new Uri(url);
            }

            public ImageMoniker Moniker { get; }

            public string Title { get; }

            public string Description { get; }

            /// <summary>
            /// What the user can do there, the text of the link.
            /// </summary>
            public string Action { get; }

            public Uri Url { get; }
        }
    }
}
