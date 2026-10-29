//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>
    /// Records how a dialog was used, as a <c>DialogClosed</c> event when it's closed: how it was closed, how long it was open,
    /// which tabs were viewed and which actions were used. Only fixed names are recorded, never what was typed or chosen.
    /// </summary>
    internal sealed class DialogTelemetry
    {
        /// <summary>The dialog was closed with the title bar button, Esc or a Close button.</summary>
        public const string OutcomeClosed = "closed";

        /// <summary>The changes were discarded.</summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>The changes were saved.</summary>
        public const string OutcomeSaved = "saved";

        private readonly string _dialog;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly SortedSet<string> _tabs = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _actions = new SortedSet<string>(StringComparer.Ordinal);

        private DialogTelemetry(Window window, string dialog, TabControl tabs)
        {
            _dialog = dialog;

            if (tabs != null)
            {
                AddTab(tabs.SelectedItem);

                tabs.SelectionChanged += (sender, e) =>
                {
                    // the selection changes of the controls in the tabs bubble up to here too
                    if (e.OriginalSource == tabs)
                    {
                        AddTab(tabs.SelectedItem);
                    }
                };
            }

            window.Closed += (sender, e) => TrackClosed();
        }

        /// <summary>
        /// How the dialog was closed, <see cref="OutcomeClosed"/> unless it's set.
        /// </summary>
        public string Outcome { get; set; } = OutcomeClosed;

        /// <summary>
        /// Starts recording the use of <paramref name="window"/>.
        /// </summary>
        /// <param name="window">The dialog.</param>
        /// <param name="dialog">Fixed name of the dialog (e.g. <c>settings</c>).</param>
        /// <param name="tabs">The tabs of the dialog, if it has them.</param>
        public static DialogTelemetry Start(Window window, string dialog, TabControl tabs = null) => new DialogTelemetry(window, dialog, tabs);

        /// <summary>
        /// Records the use of an action of the dialog, once per dialog.
        /// </summary>
        /// <param name="action">Fixed name of the action (e.g. <c>root_ca_selected</c>).</param>
        public void AddAction(string action) => _actions.Add(action);

        private void AddTab(object tab)
        {
            if ((tab as TabItem)?.Header is string header)
            {
                _tabs.Add(header.ToLowerInvariant().Replace(' ', '_').Replace("-", string.Empty));
            }
        }

        private void TrackClosed()
        {
            ExtensionTelemetry.TrackEvent("DialogClosed", new TelemetryProperties
            {
                { "dialog", _dialog },
                { "outcome", Outcome },
                { "duration_s", (int)_stopwatch.Elapsed.TotalSeconds },
                { "tabs", string.Join(",", _tabs) },
                { "tab_count", _tabs.Count },
                { "actions", string.Join(",", _actions) },
            });
        }
    }
}
