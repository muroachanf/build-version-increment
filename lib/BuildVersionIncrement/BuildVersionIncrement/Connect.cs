using System;
using Extensibility;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using System.Resources;
using System.Reflection;
using System.Globalization;
using Qreed.CodePlex;
using Qreed.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Qreed.VisualStudio;
using Qreed.Windows.Forms;

namespace BuildVersionIncrement
{
    /// <summary>The object for implementing an Add-in.</summary>
    /// <seealso class='IDTExtensibility2' />
    public class Connect : VSAddin
    {
        /// <summary>Implements the constructor for the Add-in object. Place your initialization code within this method.</summary>
        public Connect()
        {
            _logger = new Logger(this);
            _incrementor = new BuildVersionIncrementor(this);
        }

        /// <summary>
        /// Override this to create your menu items here.
        /// </summary>
        /// <returns>The root menu item.</returns>
        protected override VSMenu SetupMenuItems()
        {
            VSMenu rootMenu = new VSMenu(this, "Build Version Increment");

            VSMenuCommand versionCheck = rootMenu.AddCommand("OnlineVersionCheck", "Check for a new version.",
                                                             "Check online for a new version of this addin.");

            //versionCheck.Button.FaceId = 1922;
            versionCheck.Execute += new EventHandler(MenuVersionCheck_Execute);
            

            VSMenuCommand settingsCmd = rootMenu.AddCommand("BuildVersionIncrementSettings", "Settings",
                                                            "Configure BuildVersionIncrement.");

            //settingsCmd.Button.FaceId = 0642;
            settingsCmd.Execute += new EventHandler(DisplayAddinSettings);
            settingsCmd.QueryStatus += new EventHandler<VSMenuQueryStatusEventArgs>(MenuSettingsQueryStatus);
            return rootMenu;
        }

        void MenuSettingsQueryStatus(object sender, VSMenuQueryStatusEventArgs e)
        {
            if (ApplicationObject.Solution.IsOpen)
                e.Status = vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
            else
                e.Status = vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusInvisible;
        }

        void MenuVersionCheck_Execute(object sender, EventArgs e)
        {
            WinWrapper w = new WinWrapper(this);
            
            try
            {
                ProgressDialog dlg = new ProgressDialog();

                dlg.DoWork += new System.ComponentModel.DoWorkEventHandler(MenuVersionCheckDoWork);
                dlg.Text = "Version Check ...";
                dlg.ProgressBar.Style = ProgressBarStyle.Marquee;
                dlg.AutoClose = true;

                dlg.ShowDialog(w);

                if (dlg.Result.Exception != null)
                    throw (dlg.Result.Exception);
                else if(!dlg.Result.IsCancelled)
                {
                    VersionChecker vc = (VersionChecker)dlg.Result.Value;

                    if (vc.OnlineVersion > vc.LocalVersion)
                    {
                        DisplayVersionCheckerResult(vc);
                    }
                    else
                    {
                        MessageBox.Show(w, "Your local version is up to date.", "Version Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }

            }
            catch (Exception ex)
            {    
                MessageBox.Show(w, "Exception occured while checking for a new version:\n\n" + ex.ToString(), "Check for new version.", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisplayVersionCheckerResult(VersionChecker vc)
        {
            string message = "There's a new version available of this addin.\n\n" +
                             "Local version: " + vc.LocalVersion.ToString() + "\n" +
                             "Online version: " + vc.OnlineVersion.ToString() + "\n\n" +
                             "Would you like to open the project page on CodePlex to review the changes?";

            WinWrapper w = new WinWrapper(this);

            if (MessageBox.Show(w, message, "BuildVersionIncrement", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(vc.ProjectHomePage);
            }
        }

        void MenuVersionCheckDoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            VersionChecker vc = GetVersionChecker();
            
            e.Result = vc;
            vc.CheckForNewVersion();
        }

        /// <summary>
        /// Displays the addin settings.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void DisplayAddinSettings(object sender, EventArgs e)
        {
            AddInSettingsForm dlg = new AddInSettingsForm();
            WinWrapper w = new WinWrapper(this);

            dlg.Connect = this;
            dlg.ShowDialog(w);
        }

        #region IVSAddin Members

        /// <summary>
        /// Gets the name of the command bar resource.
        /// </summary>
        /// <value>The name of the command bar resource.</value>
        public override string CommandBarResourceName
        {
            get { return "BuildVersionIncrement.CommandBar"; }
        }

        #endregion

        private OutputWindow _outputWindow;
        /// <summary>
        /// Gets the output window.
        /// </summary>
        /// <value>The output window.</value>
        private OutputWindow OutputWindow
        {
            get
            {
                if (_outputWindow == null)
                    _outputWindow = (OutputWindow)ApplicationObject.Windows.Item(Constants.vsWindowKindOutput).Object;

                return _outputWindow;
            }
        }

        private OutputWindowPane _outputBuildWindow;
        /// <summary>
        /// Gets the output build window.
        /// </summary>
        /// <value>The output build window.</value>
        internal OutputWindowPane OutputBuildWindow
        {
            get
            {
                if (_outputBuildWindow == null)
                {
                    try
                    {
                        // this is crappy...the index is localized!
                        // so...we could use the index (seems like it's always 1),
                        // or we use the GUID {1BD8A850-02D1-11D1-BEE7-00A0C913D1F8},
                        // or we keep using "Build" for the index...which is not working...
                        // one way or the other, we're screwed...
                        _outputBuildWindow = OutputWindow.OutputWindowPanes.Item("{1BD8A850-02D1-11D1-BEE7-00A0C913D1F8}");
                    }
                    catch (ArgumentException ex)
                    {
                        Console.Write(ex.Message);
                    }
                }

                return _outputBuildWindow;
            }
        }

        private TaskList _taskList;
        /// <summary>
        /// Gets the task list.
        /// </summary>
        /// <value>The task list.</value>
        private TaskList TaskList
        {
            get
            {
                if (_taskList == null)
                    _taskList = (TaskList)ApplicationObject.Windows.Item(Constants.vsWindowKindTaskList).Object;

                return _taskList;
            }
        }

        private Logger _logger;
        private BuildVersionIncrementor _incrementor;
        private BuildEvents _buildEvents; // To prevent garbage collection of our events        

        
        /// <summary>Implements the OnConnection method of the IDTExtensibility2 interface. Receives notification that the Add-in is being loaded.</summary>
        /// <param term='application'>Root object of the host application.</param>
        /// <param term='connectMode'>Describes how the Add-in is being loaded.</param>
        /// <param term='addInInst'>Object representing this Add-in.</param>
        /// <seealso class='IDTExtensibility2' />
        public override void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom)
        {
            base.OnConnection(application, connectMode, addInInst, ref custom);

            // If you plan to use this addin with visual studio 2005 sp1 from the command line you'll need the
            // following fix.
            // http://support.microsoft.com/kb/934517
            // https://connect.microsoft.com/VisualStudio/Downloads/DownloadDetails.aspx?DownloadID=5635

            _buildEvents = ApplicationObject.Events.BuildEvents; // Add a ref to the buildevents to prevent a gc
            _buildEvents.OnBuildBegin += new _dispBuildEvents_OnBuildBeginEventHandler(_incrementor.OnBuildBegin);
            _buildEvents.OnBuildDone += new _dispBuildEvents_OnBuildDoneEventHandler(_incrementor.OnBuildDone);


            if (connectMode == ext_ConnectMode.ext_cm_Startup)
            {
#if DEBUG
                if (true)
#else
                if(DateTime.Now.Subtract(GlobalAddinSettings.Default.LastVersionCheck).Days > 1)
#endif
                {
                    Logger.Write("Checking online for a new version of BuildVersionIncrement.", LogLevel.Debug);

                    try
                    {
                        VersionChecker versionChecker = GetVersionChecker();
                        versionChecker.CheckForNewVersionComplete += new EventHandler<VersionCheckerEventArgs>(VersionChecker_CheckForNewVersionComplete);
                        versionChecker.CheckForNewVersionASync();
                    }
                    catch (Exception)
                    {
                        // Die without a sound
                    }
                }

                this._incrementor.InitializeIncrementors();
            }
        }

        private VersionChecker GetVersionChecker()
        {
            AssemblyConfigurationAttribute configAttrib = ReflectionHelper.GetAssemblyAttribute<AssemblyConfigurationAttribute>(Assembly.GetExecutingAssembly());
            return GetVersionChecker(configAttrib.Configuration);
        }

        private VersionChecker GetVersionChecker(string configuration)
        {
            VersionChecker versionChecker = new VersionChecker();

            versionChecker.ProjectHomePage = "http://autobuildversion.codeplex.com/";
            versionChecker.VersionInfoUrl = "http://autobuildversion.codeplex.com/";
            versionChecker.Assembly = Assembly.GetExecutingAssembly();
            versionChecker.Pattern = "Version\\ Info.+<table>.+?<tr><td>\\ " + configuration + "\\ </td><td>(?<Version>.+?)</td>";
            versionChecker.PatternOptions = RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase | RegexOptions.Singleline;
            versionChecker.UseAssemblyFileVersion = true;

            return versionChecker;
        }

        void VersionChecker_CheckForNewVersionComplete(object sender, VersionCheckerEventArgs e)
        {
            try
            {
                if (e.NewVersionAvailable)
                {
                    VersionChecker v = (VersionChecker)sender;

                    Logger.Write("New online version located: " + v.OnlineVersion, LogLevel.Info);

                    DisplayVersionCheckerResult(v);
                }

                GlobalAddinSettings.Default.LastVersionCheck = DateTime.Now;
                GlobalAddinSettings.Default.Save();
            }
            catch (Exception)
            {
                // Die without a sound
            }
        }

        /// <summary>Implements the OnDisconnection method of the IDTExtensibility2 interface. Receives notification that the Add-in is being unloaded.</summary>
        /// <param term='disconnectMode'>Describes how the Add-in is being unloaded.</param>
        /// <param term='custom'>Array of parameters that are host application specific.</param>
        /// <seealso class='IDTExtensibility2' />
        public override void OnDisconnection(ext_DisconnectMode disconnectMode, ref Array custom)
        {
            _buildEvents.OnBuildBegin -= _incrementor.OnBuildBegin;

            base.OnDisconnection(disconnectMode, ref custom);
        }
    }
}