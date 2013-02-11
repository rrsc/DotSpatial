﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NuGet;
using System.Windows.Forms;
using System.Net;
using DotSpatial.Controls;
using System.Security.Permissions;
using System.Security;
using System.Threading.Tasks;
using DotSpatial.Controls.Extensions;
using System.IO;
using System.Reflection;
using DotSpatial.Extensions;
using System.Collections.Specialized;

namespace DotSpatial.Plugins.ExtensionManager
{
    public class Update
    {
        public Update(Packages package, ListViewHelper adder, AppManager Appmanager)
        {
            this.packages = package;
            this.Add = adder;
            this.App = Appmanager;
        }

        private readonly Packages packages;
        private readonly ListViewHelper Add;
        private AppManager App;
        private GetPackage getpack;
        private ListView listview;
        private TabPage tab;
        private IEnumerable<IPackage> list = null;
        private const string HideReleaseFromEndUser = "HideReleaseFromEndUser";
        private const string HideFromAutoUpdate = "HideFromAutoUpdate";

        //Function that refreshes the listview in the updates tab.
        public void RefreshUpdate(ListView lv, TabPage tp)
        {
            listview = lv;
            tab = tp;
            getAvailableUpdates();

            //Refresh the list view with the updates found.
            if (list != null) { setListView(); }
        }

        private void getAvailableUpdates()
        {
            list = null;

            //Look for packages to be updated in the folder where Extension Manager downloads new packages.
            getpack = new GetPackage(packages);
            IEnumerable<IPackage> localPackages = getpack.GetPackagesFromExtensions(App.Extensions);
            List<String> packageNames = null;
            if (localPackages.Count() > 0)
            {
                getAvailableUpdatesFromLocal(localPackages);
                packageNames = getPackageNames(localPackages);
            }

            //Find other packages that may need updating by looking at the current feed and comparing to installed packages.
            getAvailableUpdatesFromFeed(packageNames);
        }

        //Using the class variable 'list' to refresh the packages that are eligble for update.
        private void setListView()
        {
            int Count = list.Count();

            try
            {
                listview.Invoke((Action)(() =>
                {
                    listview.Clear();
                    Add.AddPackages(list, listview, 0);
                    tab.Text = String.Format("Updates ({0})", Count);
                    if (Count == 0)
                    {
                        listview.Clear();
                        listview.Items.Add("No updates available for the selected feed.");
                    }
                }));
            }
            catch (Exception e)
            {               
            }
        }

        //Looks in the folder where extensions are saved (when downloaded through the Ext Manager) and determines in updates are needed.
        private void getAvailableUpdatesFromLocal(IEnumerable<IPackage> localPackages)
        {
            try
            {
                list = packages.Repo.GetUpdates(localPackages, false, false);
            }
            catch (WebException)
            {
                if (listview != null && tab != null)
                {
                    listview.Invoke((Action)(() =>
                    {
                        listview.Clear();
                        tab.Text = "Update";
                        listview.Items.Add("Updates could not be retrieved for the selected feed.");
                        listview.Items.Add("Try again later or change the feed.");
                    }));
                }
            }
        }

        //Looks at all packages from the current feed, finds local version of it (if any), compares versions.
        //packageNames are all packages found and checked by getUpdatesFromLocal.
        private void getAvailableUpdatesFromFeed(List<String> packageNames)
        {
            IEnumerable<IPackage> onlinePacks = null;
            List<IPackage> updatePacks = new List<IPackage>();
            Task<PackageList> task = getAllPackagesFromFeed();

            //Get list of packages from current feed.
            task.ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    onlinePacks = t.Result.packages;
                }
            }).Wait();
            try
            {
                foreach (IPackage pack in onlinePacks)
                {
                    if (IsPackageUpdateable(pack))
                    {
                        //If packageNames has no names, just add all packages that are updateable.
                        if (packageNames == null)
                        {
                            updatePacks.Add(pack);
                        }
                        else if (!packageNames.Contains(pack.Id)) //If there are packageNames, then make sure we are not adding a package we've already checked.
                        {
                            updatePacks.Add(pack);
                        }
                    }
                }
                if (list == null)
                {
                    list = updatePacks;
                }
                else
                {
                    list = list.Concat(updatePacks);
                }
            }
            catch { }
        }

        //Return list of all packages from currently selected feed.
        private Task<PackageList> getAllPackagesFromFeed()
        {
            var task = Task.Factory.StartNew(() =>
            {
                try
                {
                    var result = from item in packages.Repo.GetPackages()
                                 where item.IsLatestVersion && (item.Tags == null 
                                 || (!item.Tags.Contains(HideReleaseFromEndUser)))
                                 select item;

                    var info = new PackageList();
                    info.TotalPackageCount = result.Count();
                    info.packages = result.ToArray();
                    return info;
                }
                catch (InvalidOperationException)
                {
                    // This usually means the url was bad.
                    return null;
                }
            });
            return task;
        }

        public static void doUpdate(AppManager app)
        {
            bool updateOccurred = false;
            Packages packages = new Packages();
            System.Collections.Specialized.StringCollection feeds = FeedManager.getAutoUpdateFeeds();
            foreach (String feed in feeds)
            {
                packages.SetNewSource(feed);
                Update update = new Update(packages, null, app);
                if (update.autoUpdate()) { updateOccurred = true; }
            }

            if (updateOccurred)
            {
                MessageBox.Show("Extensions have been updated.\nUpdate will be complete when HydroDesktop is restarted.");
            }
        }

        //autoUpdate any packages found.
        internal bool autoUpdate()
        {
            bool updateOccurred = false;
            getAvailableUpdates();

            if (list != null)
            {
                foreach (IPackage p in list)
                {
                    if ((p.Tags == null) || (!p.Tags.Contains(HideFromAutoUpdate))) 
                    { 
                        UpdatePack(p);
                        updateOccurred = true;
                    }
                }
            }
            return updateOccurred;
        }

        internal void UpdatePack(IPackage pack)
        {
            if (pack == null) return;

            // deactivate the old version and mark for uninstall
            //var extension = App.EnsureDeactivated(pack.Id);
            var extension = App.GetExtension(pack.Id);
                    
            App.MarkExtensionForRemoval(GetExtensionPath(extension));           

            App.ProgressHandler.Progress(null, 0, "Updating " + pack.Title);

            // get new version
            try
            {
                packages.Update(pack);
                if (IsPackageInstalled(pack))
                {
                    App.MarkPackageForRemoval(GetPackagePath(pack));
                }
            }
            catch(Exception e)
            {
                throw e;
            }
            finally
            {
                App.ProgressHandler.Progress(null, 0, "");
            }
        }

        //Determines if a package has a local version that can be deleted and is out of date.
        private bool IsPackageUpdateable(IPackage pack)
        {
            // Is there a local version?
            var ext = App.GetExtension(pack.Id);
            if (ext == null) return false;

            //Checks if current version is greater than or equal to posted version, return false if true.
            //Assumes that version numbers in assembly files are accurate (not currently true).
            SemanticVersion extVersion = SemanticVersion.Parse(ext.Version);
            if (extVersion >= pack.Version) return false;
            string assemblyLocation = GetExtensionPath(ext);

            // The original file may be in a different location than the extensions directory. During an update, the
            // original file will be deleted and the new package will be placed in the packages folder in the extensions
            // directory.
            FileIOPermission deletePermission = new FileIOPermission(FileIOPermissionAccess.Write, assemblyLocation);

            try
            {
                deletePermission.Demand();
            }
            catch (SecurityException)
            {
                return false;
            }

            return true;
        }

        internal static string GetExtensionPath(IExtension ext)
        {
            var assembly = Assembly.GetAssembly(ext.GetType());
            return assembly.Location;
        }

        internal static bool IsPackageInstalled(IPackage pack)
        {
            string path = GetPackagePath(pack);
            return Directory.Exists(path);
        }

        internal static string GetPackagePath(IPackage pack)
        {
            string path = Path.Combine(AppManager.AbsolutePathToExtensions, AppManager.PackageDirectory, GetPackageFolderName(pack));
            return path;
        }

        internal static string GetPackageFolderName(IPackage pack)
        {
            return String.Format("{0}.{1}", pack.Id, pack.Version);
        }

        
        private List<String> getPackageNames(IEnumerable<IPackage> packs)
        {
            List<String> packNames = new List<String>();
            foreach (IPackage p in packs)
            {
                packNames.Add(p.Id);
            }
            return packNames;
        }
    }
}