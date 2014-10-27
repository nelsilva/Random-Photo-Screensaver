﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using System.Data;
using System.Diagnostics;

namespace RPS {
    public class FileNodes {
        System.ComponentModel.BackgroundWorker backgroundWorker;
        bool restartBackgroundWorker = false;
        FileDatabase fileDatabase;
        //FileDatabase metaDatabase;

        ExifTool.Wrapper exifToolWorker;          // Used in background worker
        ExifTool.Wrapper exifToolMain;  // Used in foreground

        Config config;
        Screensaver screensaver;

        int nrFolders = 0;
        int nrFiles = 0;

        int nrUnprocessedMetadata = -1;

        System.Diagnostics.Stopwatch swFileScan;
        System.Diagnostics.Stopwatch swMetadata;

        public long currentSequentialSeedId = -1;

        object bwSender;
        DoWorkEventArgs bwEvents;

        public FileNodes(Config config, Screensaver screensaver) {
            this.config = config;
            this.screensaver = screensaver;
            this.fileDatabase = new FileDatabase();
            //this.fileDatabase.MetadataReadEvent += new MetadataReadEventHandler(metadataShow);

            this.backgroundWorker = new System.ComponentModel.BackgroundWorker();
            this.backgroundWorker.WorkerReportsProgress = true;
            this.backgroundWorker.WorkerSupportsCancellation = true;
            this.backgroundWorker.DoWork += new DoWorkEventHandler(DoWorkImageFolder);
            this.backgroundWorker.ProgressChanged += new ProgressChangedEventHandler(progressChanged);
            this.backgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(runWorkerCompleted);

            this.backgroundWorker.RunWorkerAsync();
        }
        /*
        void metadataShow(ImageMetadataStatus imageMetadata) {
            this.screensaver.monitors[imageMetadata.monitorId].showMetadataOnMonitor(imageMetadata.metadata);
            //this.Invoke()
            //this.MetadataReadEvent(imageMetadata);
            //            this.screensaver.monitors[imageMetadata.monitorId].showMetadataOnMonitor(imageMetadata.metadata);
            //this.screensaver.monitors[imageMetadata.monitorId].showImage(false);
        }*/

        public void setFilterSQL(string sql) {
            this.fileDatabase.setFilterSQL(sql);
        }

        public void clearFilter() {
            this.fileDatabase.clearFilter();
        }

        public void exifToolWorkerStarted() {
            if (this.exifToolWorker == null) {
                this.exifToolWorker = new ExifTool.Wrapper(this.config.getValue("exifTool"));
                this.exifToolWorker.Starter();
            }
        }

        public void exifToolMainStarted() {
            if (this.exifToolMain == null) {
                this.exifToolMain = new ExifTool.Wrapper(this.config.getValue("exifTool"));
                this.exifToolMain.Starter();
            }
        }

        private bool bwCancelled() {
            if ((backgroundWorker.CancellationPending == true)) {
                this.bwEvents.Cancel = true;
                return true;
            } else {
                BackgroundWorker worker = this.bwSender as BackgroundWorker;
                try {
                    worker.ReportProgress(10);
                } catch (System.InvalidOperationException ioe) {
                    // Thrown when out of sync on application exit
                }
                return false;
            }
        }

        public void restartBackgroundWorkerImageFolder() {
            if (this.backgroundWorker.IsBusy) {
                this.restartBackgroundWorker = true;
                this.backgroundWorker.CancelAsync();
            } else {
                this.restartBackgroundWorker = false;
                this.backgroundWorker.RunWorkerAsync();
            }
        }

        private void processFolders(List<string> folders) {
            while (folders.Count > 0) {
                //Debug.WriteLine(folders[0]);
                if (this.bwCancelled() == true) return;

                folders[0] = folders[0].Trim();

                if (Directory.Exists(folders[0])) {
                    this.nrFolders++;
                    HtmlElement he;
                    string allowedExtensions;
                    bool ignoreHiddenFiles;
                    bool ignoreHiddenFolders;

                    try {
                        he = this.config.getElementById("imageExtensions");
                        allowedExtensions = this.config.getValue("imageExtensions").ToLower() + " " + this.config.getValue("videoExtensions").ToLower();
                        ignoreHiddenFiles = this.config.getCheckboxValue("ignoreHiddenFiles");
                        ignoreHiddenFolders = this.config.getCheckboxValue("ignoreHiddenFolders");
                    } catch (System.Runtime.InteropServices.InvalidComObjectException icoe) {
                        // Occurs when shutting down, cancel thread
                        this.bwCancelled();
                        return;
                    }

                    string[] filenames = new string[] { };
                    try {
                        filenames = Directory.GetFiles(folders[0]);
                    } catch (System.UnauthorizedAccessException uae) {
                        folders.RemoveAt(0);
                    }
                    var i = 0;
                    foreach (string filename in filenames) {
                        i++;
                        FileInfo fi = new FileInfo(filename);
                        if (
                            // Image has to have extension
                            (fi.Extension.Length > 0) &&
                            // Check allowed file extensions
                            (allowedExtensions.IndexOf(fi.Extension.ToLower()) > -1) &&
                            // Ignore hidden files if option checked
                            (!ignoreHiddenFiles || (ignoreHiddenFiles && (fi.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden))
                        ) {
                            this.nrFiles++;
                            if ((i % 10 == 0) && (this.bwCancelled() == true)) break;
                            this.fileDatabase.addFileToDB(fi);
                        }
                    }
                    string[] subfolders = new string[] { };
                    try {
                        subfolders = Directory.GetDirectories(folders[0]);
                    } catch (System.UnauthorizedAccessException uae) { }
                    i = 0;
                    foreach (string subfolder in subfolders) {
                        FileAttributes fa = File.GetAttributes(subfolder);
                        // Ignore hidden folders if option checked
                        if (!ignoreHiddenFolders || (ignoreHiddenFolders && (fa & FileAttributes.Hidden) != FileAttributes.Hidden)) {
                            i++;
                            folders.Add(subfolder);
                        }
                        if ((i % 100 == 0) && (this.bwCancelled() == true)) break;
                    }
                }
                folders.RemoveAt(0);
            }
        }
/*
        public string processMetadata(DataRow dr) {
            return this.processMetadata(dr, true);
        }

        public string processMetadata(DataRow dr, bool getSingleValue) {
            int i = 0;
            string meta = null;
            while (dr != null) {
                if (i % 50 == 0) this.fileDatabase.toggleMetadataTransaction();
                this.exifToolStarted();
                meta = this.exifTool.SendCommand(Convert.ToString(dr["path"]));
                this.fileDatabase.addMetadataToDB(Convert.ToInt32(dr["id"]), meta);
                this.nrUnprocessedMetadata--;

                if (this.bwCancelled() == true) return null;

                if (getSingleValue) dr = null;
                else dr = this.fileDatabase.nextMetadataLessImage();
                i++;
            }
            this.fileDatabase.toggleMetadataTransaction();
            return meta;
        }

        public void processMetadata() {
            this.nrUnprocessedMetadata = this.fileDatabase.nrMetadataImagesToProcess();
            DataRow dr;
            dr = this.fileDatabase.nextMetadataLessImage();
            this.processMetadata(dr, false);
        }*/

        public string exifToolCommand(string command, long imageId) {
            this.exifToolMainStarted();
            string metadata = this.exifToolMain.SendCommand(command);
            if (metadata != null) this.fileDatabase.addMetadataToDB(imageId, metadata);
            return metadata;
        }

        public void processMetadata() {
            this.nrUnprocessedMetadata = this.fileDatabase.nrMetadataImagesToProcess();
            DataRow dr;
            dr = this.fileDatabase.nextMetadataLessImage();
            int i = 0;
            string meta = null;
            while (dr != null) {
                if (i % 50 == 0) this.fileDatabase.toggleMetadataTransaction();
                this.exifToolWorkerStarted();
                meta = this.exifToolWorker.SendCommand(Convert.ToString(dr["path"]));
                /*              // Alternative using exiv2, slightly (10% - 15%) quicker but output needs more processing (not tab deliminated)
                                Process proc = new Process {
                                    StartInfo = new ProcessStartInfo {
                                        FileName = @"D:\programming\vc#\RPS 4\RPS 4\vendor\exiv2_32.exe",
                                        Arguments = "-PEIXkvt \"" + Convert.ToString(dr["path"]) + "\"",
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        CreateNoWindow = true
                                    }
                                };
                                proc.Start();
                                string meta = proc.StandardOutput.ReadToEnd();
                                proc.WaitForExit();
                */
                this.fileDatabase.addMetadataToDB(Convert.ToInt32(dr["id"]), meta);
                this.nrUnprocessedMetadata--;

                if (this.bwCancelled() == true) return;

                dr = this.fileDatabase.nextMetadataLessImage();
                i++;
            }
            this.fileDatabase.toggleMetadataTransaction();
        }


        public void debugMonitorInfo(int m, SortOrder d, int o, DataRow dr, string s) {
            if (dr == null) this.screensaver.monitors[m].showInfoOnMonitor("getSequentialImage(monitor " + m + ", direction " + d.ToString() + ", offset "+o+") ["+s+"]: null");
            else this.screensaver.monitors[m].showInfoOnMonitor("getSequentialImage(monitor " + m + ", direction " + d.ToString() + ", offset " + o + ") [" + s + "]: " + dr["id"]);
        }

        public DataRow getFirstImage(int monitor) {
            string[] imageIds;
            string s = this.config.getValue("randomStartImages");
            if (s != null && s.Length > 0) {
                imageIds = s.Split(';');
                long imageId;
                try {
                    imageId = Convert.ToInt32(imageIds[monitor]);
                } catch (Exception e) {
                    imageId = -1;
                }
                return this.fileDatabase.getImageById(imageId, 0);
            }
            return null;
        }

        public DataRow getSequentialImage(int monitor, SortOrder direction, int offset) {
            DataRow currentImage = null;

            string sortBy = this.screensaver.config.getRadioValue("sortBy");
            SortOrder sortDirection = new SortOrder(this.screensaver.config.getRadioValue("sortDirection"));

            // All clear, proceed
            if (this.currentSequentialSeedId == -1) {
                long imageId;
                try {
                    imageId = Convert.ToInt32(this.config.getValue("sequentialStartImageId"));
                } catch(Exception e){
                    imageId = -1;
                }
                currentImage = this.fileDatabase.getFirstImage(imageId, sortBy, sortDirection);
            } else {
                //                                               getImageById(long id, long offset, long direction, string sortBy, string orderingTerm) {
                currentImage = fileDatabase.getImageById(this.currentSequentialSeedId, offset, direction, sortBy, sortDirection);
            }
            if (currentImage != null) {
                this.currentSequentialSeedId = Convert.ToInt32(currentImage["id"]);
            }
            this.debugMonitorInfo(monitor, direction, offset, currentImage, "currentImage");
            return currentImage;
        }

        public DataRow getImageById(long id, long historyOffset) {
            return fileDatabase.getImageById(id, historyOffset);
        }

        public DataRow getRandomImage() {
            return fileDatabase.getRandomImage();
        }
        
        public string getMetadataById(long id) {
            return this.fileDatabase.getMetadataById(id);
        }

        public int deleteFromDB(string path) {
            return this.fileDatabase.deleteFromDB(path);
        }

        public int deleteFromDB(long id) {
            return this.fileDatabase.deleteFromDB(id);
        }
/*
        public void addIdToMetadataQueue(long monitorId, DataRow image) {
            this.fileDatabase.addIdToMetadataQueue(monitorId, image);
        }
        */
        private void DoWorkImageFolder(object sender, DoWorkEventArgs e) {
//            Debug.WriteLine(this.config.getValue("folders"));
            BackgroundWorker worker = sender as BackgroundWorker;
            // Lower priority to ensure smooth working of main screensaver
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
            this.bwSender = sender;
            this.bwEvents = e;
            string f = this.config.getValue("folders");
            string[] ff = new string[] {};
            if (f != null && f.Length > 0) {
                ff = f.Split(new string[] { ";", System.Environment.NewLine, "\n" }, StringSplitOptions.None);
            }
            var folders = new List<string>(ff);
            this.swFileScan = new System.Diagnostics.Stopwatch();
            this.swMetadata = new System.Diagnostics.Stopwatch();
            this.swFileScan.Start();
            this.processFolders(folders);
            this.swFileScan.Stop();
            this.swMetadata.Start();
            this.processMetadata();
            this.swMetadata.Stop();
            //if (Convert.ToDateTime(this.config.setValue("wallpaperLastChange")).Equals(DateTime.Today));
            Wallpaper wallpaper = new Wallpaper(this.screensaver);
            if (wallpaper.changeWallpaper()) wallpaper.setWallpaper();
/*
            var command = new SQLiteCommand(conn);
            command.CommandText = @"SELECT COUNT(id) FROM `FileNodes`;";
            //SQLiteDataReader reader = command.ExecuteReader();
            //while (reader.Read()) {
            Debug.WriteLine("Rows in DB: " + Convert.ToInt32(command.ExecuteScalar()));
            */
        }

        private void progressChanged(object sender, ProgressChangedEventArgs e) {
            string info = "";// = "No files found in folder(s) - Press 'S' key to show configuration screen";
            long nrImagesFiltered = this.fileDatabase.nrImagesFilter();
            long nrImagesInDb = this.fileDatabase.nrImagesInDB();
            if (nrImagesFiltered > 0 || nrImagesInDb > 0) {
                if (this.screensaver.config.getCheckboxValue("useFilter")) {
                    info += String.Format("DB {0:##,#}, filter {1:##,#} images; ", this.fileDatabase.nrImagesInDB(), this.fileDatabase.nrImagesFilter(), this.nrFiles, this.nrFolders);
                } else {
                    info += String.Format("DB {0:##,#} images; ", this.fileDatabase.nrImagesInDB());
                }
            }
            if (this.nrUnprocessedMetadata >= 0) {
                info += String.Format(" Metadata queue {0:##,#} files", this.nrUnprocessedMetadata);
            } else {
                if (this.nrFiles > 0 || this.nrFolders > 0) {
                    info += String.Format("Scanned {0:##,#} files in {1:##,#} folders", this.nrFiles, this.nrFolders);
                }
            }
            try {
                this.screensaver.monitors[0].browser.Document.InvokeScript("dbInfo", new String[] { info });
            } catch (NullReferenceException nre) {

            }
        }

        private void runWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
            if ((e.Cancelled == true)) {
                if (this.restartBackgroundWorker) {
                    this.restartBackgroundWorker = false;
                    this.backgroundWorker.RunWorkerAsync();
                    Debug.WriteLine("BackgroundWorker Restarted");
                } else {
                    Debug.WriteLine("BackgroundWorker Canceled!");
                }
            } else if (!(e.Error == null)) {
                Debug.WriteLine("BackgroundWorker Error: " + e.Error.Message);
            } else {
                if (this.screensaver.fileNodes.fileDatabase.nrImagesFilter() == 0) {
                    this.screensaver.showInfoOnMonitors("No images found in folder(s)\n\ror filter didn't return any results.\n\rPress 'S' key to enter setup", true);
                }
                this.screensaver.monitors[0].browser.Document.InvokeScript("dbInfo", new String[] { String.Format("Found {0:##,#} files in {1:##,#} folders ({3:##,#}ms); Metadata queue {2:##,#} files ({4:##,#}ms)", this.nrFiles, this.nrFolders, this.nrUnprocessedMetadata, this.swFileScan.ElapsedMilliseconds, this.swMetadata.ElapsedMilliseconds) });
                //Debug.WriteLine("BackgroundWork done!");
            }
        }

        public void OnExitCleanUp() {
            this.fileDatabase.storePersistant();
        }
    }
}