﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TabControl;
using Terminals.Configuration;
using Terminals.Connections;
using Terminals.Data;

namespace Terminals
{
    /// <summary>
    /// Adapter between all windows (including main window) and TabControl
    /// </summary>
    internal class TerminalTabsSelectionControler
    {
        private TabControl.TabControl mainTabControl;
        private MainForm mainForm;
        private List<PopupTerminal> detachedWindows = new List<PopupTerminal>();

        internal TerminalTabsSelectionControler(MainForm mainForm, TabControl.TabControl tabControl)
        {
            this.mainTabControl = tabControl;
            this.mainForm = mainForm;
            Persistance.Instance.Dispatcher.FavoritesChanged += new FavoritesChangedEventHandler(this.OnFavoritesChanged);
        }

        private void OnFavoritesChanged(FavoritesChangedEventArgs args)
        {
            foreach (IFavorite updated in args.Updated)
            {
                // dont update the rest of properties, because it doesnt reflect opened session
                UpdateDetachedWindowTitle(updated);
                UpdateAttachedTabTitle(updated);
            }
        }

        private void UpdateAttachedTabTitle(IFavorite updated)
        {
            TabControlItem attachedTab = this.FindAttachedTab(updated);
            if (attachedTab != null)
                attachedTab.Title = updated.Name;
        }

        private TabControlItem FindAttachedTab(IFavorite updated)
        {
            return this.mainTabControl.Items.Cast<TerminalTabControlItem>()
                .FirstOrDefault(tab => tab.Favorite.Equals(updated));
        }

        private void UpdateDetachedWindowTitle(IFavorite updated)
        {
            PopupTerminal detached = this.FindDetachedWindowByFavorite(updated);
            if (detached != null)
                detached.UpdateTitle();
        }

        private PopupTerminal FindDetachedWindowByFavorite(IFavorite updated)
        {
            return this.detachedWindows.FirstOrDefault(window => window.Favorite.Equals(updated));
        }

        /// <summary>
        /// Markes the selected terminal as selected. If it is in mainTabControl,
        /// then directly selects it, otherwise marks the selected window
        /// </summary>
        /// <param name="toSelect">new terminal tabControl to assign as selected</param>
        internal void Select(TerminalTabControlItem toSelect)
        {
            this.mainTabControl.SelectedItem = toSelect;
        }

        /// <summary>
        /// Clears the selection of currently manipulated TabControl.
        /// This has the same result like to call Select(null).
        /// </summary>
        internal void UnSelect()
        {
            Select(null);
        }

        internal void AddAndSelect(TerminalTabControlItem toAdd)
        {
            this.mainTabControl.Items.Add(toAdd);
            this.Select(toAdd);
        }

        internal void RemoveAndUnSelect(TerminalTabControlItem toRemove)
        {
            this.mainTabControl.Items.Remove(toRemove);
            this.UnSelect();
        }

        /// <summary>
        /// Gets the actualy selected TabControl even if it is not in main window
        /// </summary>
        internal TerminalTabControlItem Selected
        {
            get
            {
                return this.mainTabControl.SelectedItem as TerminalTabControlItem;
            }
        }

        internal bool HasSelected
        {
            get
            {
                return this.mainTabControl.SelectedItem != null;
            }
        }

        /// <summary>
        /// Releases actualy selected tab to the new window
        /// </summary>
        internal void DetachTabToNewWindow()
        {
            this.DetachTabToNewWindow(this.Selected);
        }

        internal void DetachTabToNewWindow(TerminalTabControlItem tabControlToOpen)
        {
            if (tabControlToOpen != null)
            {
                this.mainTabControl.Items.SuspendEvents();

                PopupTerminal pop = new PopupTerminal(this);
                mainTabControl.RemoveTab(tabControlToOpen);
                pop.AddTerminal(tabControlToOpen);

                this.mainTabControl.Items.ResumeEvents();
                this.detachedWindows.Add(pop);
                pop.Show();
            }
        }

        internal void AttachTabFromWindow(TerminalTabControlItem tabControlToAttach)
        {
            this.mainTabControl.AddTab(tabControlToAttach);
            PopupTerminal popupTerminal = tabControlToAttach.FindForm() as PopupTerminal;
            if (popupTerminal != null)
            {
                UnRegisterPopUp(popupTerminal);
            }
        }

        internal void UnRegisterPopUp(PopupTerminal popupTerminal)
        {
            if (this.detachedWindows.Contains(popupTerminal))
            {
                this.detachedWindows.Remove(popupTerminal);
            }
        }

        internal void RefreshCaptureManagerAndCreateItsTab(bool openManagerTab)
        {
            Boolean createNew = !this.RefreshCaptureManager(true);

            if (createNew) // capture manager wasnt found
            {
                if (!openManagerTab && (!Settings.EnableCaptureToFolder || !Settings.AutoSwitchOnCapture))
                    createNew = false;
            }

            if (createNew)
            {
                this.CreateCaptureManagerTab();
            }
        }

        /// <summary>
        /// Updates the CaptureManager tabcontrol, focuses it and updates its content.
        /// </summary>
        /// <param name="setFocus">If true, focuses the capture manager Tab; otherwise nothting</param>
        /// <returns>true, Tab exists and was updated, otherwise false.</returns>
        internal Boolean RefreshCaptureManager(Boolean setFocus)
        {
            foreach (TerminalTabControlItem tab in this.mainTabControl.Items)
            {
                if (tab.Title == Program.Resources.GetString("CaptureManager"))
                {
                    CaptureManagerConnection conn = (tab.Connection as CaptureManagerConnection);
                    conn.RefreshView();
                    if (setFocus && Settings.EnableCaptureToFolder && Settings.AutoSwitchOnCapture)
                    {
                        conn.BringToFront();
                        conn.Update();
                        this.Select(tab);
                    }

                    return true;
                }
            }

            return false;
        }

        private void CreateCaptureManagerTab()
        {
            string captureTitle = Program.Resources.GetString("CaptureManager");
            TerminalTabControlItem terminalTabPage = new TerminalTabControlItem(captureTitle);
            try
            {
                terminalTabPage.AllowDrop = false;
                terminalTabPage.ToolTipText = captureTitle;
                terminalTabPage.Favorite = null;
                terminalTabPage.DoubleClick += new EventHandler(this.mainForm.terminalTabPage_DoubleClick);
                this.AddAndSelect(terminalTabPage);
                this.mainForm.UpdateControls();

                IConnection conn = new CaptureManagerConnection();
                conn.TerminalTabPage = terminalTabPage;
                conn.ParentForm = this.mainForm;
                conn.Connect();
                (conn as Control).BringToFront();
                (conn as Control).Update();

                this.mainForm.UpdateControls();
            }
            catch (Exception exc)
            {
                Logging.Log.Error("Error loading the Capture Manager Tab Page", exc);
                this.RemoveAndUnSelect(terminalTabPage);
                terminalTabPage.Dispose();
            }
        }

        internal void UpdateCaptureButtonOnDetachedPopUps()
        {
            bool newEnable = Settings.EnabledCaptureToFolderAndClipBoard;
            foreach (PopupTerminal detachedWindow in this.detachedWindows)
            {
                detachedWindow.UpdateCaptureButtonEnabled(newEnable);
            }
        }
    }
}
