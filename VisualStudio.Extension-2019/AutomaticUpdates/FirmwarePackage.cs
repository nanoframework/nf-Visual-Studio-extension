//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.VisualStudio.Extension.FirmwareUpdate
{
    /// <summary>
    /// Abstract base class that handles the download and extraction of firmware file from Bintray.
    /// </summary>
    internal abstract class FirmwarePackage : IDisposable
    {
        // HttpClient is intended to be instantiated once per application, rather than per-use.
        static HttpClient _bintrayClient = new HttpClient();

        /// <summary>
        /// Uri of Bintray API
        /// </summary>
        internal const string _bintrayApiPackages = "https://api.bintray.com/packages/nfbot";

        internal const string _refTargetsDevRepo = "nanoframework-images-dev";
        internal const string _refTargetsStableRepo = "nanoframework-images";
        internal const string _communityTargetsepo = "nanoframework-images-community-targets";

        internal string _targetName;
        internal string _fwVersion;
        internal bool _stable;

        internal const string _readmeContent = "This folder contains .NET nanoFramework firmware files. Can safely be removed.";

        private string _versionRaw;
        private Version _version;

        /// <summary>
        /// Path with the location of the downloaded firmware.
        /// </summary>
        public string LocationPath { get; internal set; }

        /// <summary>
        /// Version of the available firmware.
        /// </summary>
        public Version Version
        {
            get
            {
                if (_version == null)
                {
                    // try to parse raw version 
                    try
                    {
                        _version = new Version(_versionRaw.Replace("-preview", ""));
                    }
                    catch
                    {
                        // failed to build a version from the raw format
                    }
                }

                return _version;
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="targetName">Target name as designated in the repositories.</param>
        protected FirmwarePackage(string targetName, string fwVersion, bool stable)
        {
            _targetName = targetName;
            _fwVersion = fwVersion;
            _stable = stable;
        }

        /// <summary>
        /// Download the firmware zip, extract this zip file, and get the firmware parts
        /// </summary>
        /// <returns>a dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</returns>
        protected async System.Threading.Tasks.Task<bool> DownloadAndExtractAsync()
        {
            string fwFileName = null;
            var repoName = _stable ? _refTargetsStableRepo : _refTargetsDevRepo;

            // flag to signal if the work-flow step was successful
            bool stepSuccesful = false;
            
            // flag to skip download if the fw package exists and it's recent
            bool skipDownload = false;

            // setup download folder
            // set download path
            LocationPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nanoFramework");

            try
            {
                // create home directory
                // create directory
                Directory.CreateDirectory(LocationPath);

                // add readme file
                File.WriteAllText(
                    Path.Combine(
                        LocationPath,
                        "README.txt"),
                    _readmeContent);

                // set location path to target folder
                LocationPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nanoFramework",
                    _targetName);

                // create directory
                Directory.CreateDirectory(LocationPath);

                Console.WriteLine($"Download location is {LocationPath}");
            }
            catch
            {
                return false;
            }

            var fwFiles = Directory.EnumerateFiles(LocationPath, $"{_targetName}-*.zip").OrderByDescending(f => f).ToList();

            if (fwFiles.Any())
            {
                // get file creation date (from the 1st one)
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(fwFiles.First())).TotalHours < 4)
                {
                    // fw package has less than 4 hours
                    // skip download
                    skipDownload = true;
                }
            }

            if (!skipDownload)
            {
                // try to perform request
                try
                {
                    // reference targets
                    string requestUri = $"{_bintrayApiPackages}/{repoName}/{_targetName}";

                    Console.Write($"Trying to find {_targetName} in {(_stable ? "stable" : "developement")} repository...");

                    HttpResponseMessage response = await _bintrayClient.GetAsync(requestUri);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Console.Write($"Trying to find {_targetName} in community targets repository...");

                        // try with community targets
                        requestUri = $"{_bintrayApiPackages}/{_communityTargetsepo}/{_targetName}";
                        repoName = _communityTargetsepo;

                        response = await _bintrayClient.GetAsync(requestUri);

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            // can't find this target
                            return false;
                        }
                    }

                    Console.Write($"Downloading firmware package...");

                    // read and parse response
                    string responseBody = await response.Content.ReadAsStringAsync();
                    BintrayPackageInfo packageInfo = JsonConvert.DeserializeObject<BintrayPackageInfo>(responseBody);

                    // if no specific version was requested, use latest available
                    if (string.IsNullOrEmpty(_fwVersion))
                    {
                        _fwVersion = packageInfo.LatestVersion;
                    }

                    // set exposed property
                    _versionRaw = _fwVersion;

                    stepSuccesful = true;
                }
                catch
                {
                    // exception with download, assuming it's something with network connection or Bintray API
                }
            }

            // cleanup any fw file in the folder
            var filesToDelete = Directory.EnumerateFiles(LocationPath, "*.bin").ToList();
            filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.hex").ToList());
            filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.s19").ToList());
            filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.dfu").ToList());

            foreach(var file in filesToDelete)
            {
                File.Delete(file);
            }

            // check for file existence or download one
            if (stepSuccesful &&
                !skipDownload)
            {
                // reset flag
                stepSuccesful = false;

                fwFileName = $"{_targetName}-{_fwVersion}.zip";

                // check if we already have the file
                if (!File.Exists(
                    Path.Combine(
                        LocationPath,
                        fwFileName)))
                { 
                    Console.WriteLine($"Downloading {_fwVersion}");

                    try
                    {
                        // setup and perform download request
                        string requestUri = $"https://dl.bintray.com/nfbot/{repoName}/{fwFileName}";

                        MessageCentre.OutputFirmwareUpdateMessage($"[{_targetName}] Trying to download firmware package...");

                        using (var fwFileResponse = await _bintrayClient.GetAsync(requestUri))
                        {
                            if (fwFileResponse.IsSuccessStatusCode)
                            {
                                using (var readStream = await fwFileResponse.Content.ReadAsStreamAsync())
                                {
                                    using (var fileStream = new FileStream(
                                        Path.Combine(LocationPath, fwFileName),
                                        FileMode.Create, FileAccess.Write))
                                    {
                                        await readStream.CopyToAsync(fileStream);
                                    }
                                }

                            }
                            else
                            {
                                MessageCentre.OutputFirmwareUpdateMessage($"[{_targetName}] ERROR: Failed to download file from repository.");

                                return false;
                            }
                        }

                        MessageCentre.OutputFirmwareUpdateMessage($"[{_targetName}] Download OK.");

                        stepSuccesful = true;
                    }
                    catch
                    {
                        // exception with download, assuming it's something with network connection or Bintray API
                    }
                }
                else
                {
                    // file already exists
                    stepSuccesful = true;
                }
            }

            if(!stepSuccesful)
            {
                // couldn't download the fw file
                // check if there is one available
                fwFiles = Directory.EnumerateFiles(LocationPath, $"{_targetName}-*.zip").OrderByDescending(f => f).ToList();

                if(fwFiles.Any())
                {
                    // take the 1st one
                    fwFileName = fwFiles.First();

                    // get the version form the file name
                    var pattern = @"(\d+\.\d+\.\d+)(\.\d+|-.+)(?=\.zip)";
                    var match = Regex.Matches(fwFileName, pattern, RegexOptions.IgnoreCase);

                    // set property
                    _versionRaw = match[0].Value;

                    MessageCentre.OutputFirmwareUpdateMessage($"[{_targetName}] Using cached firmware package...");
                }
                else
                {
                    // no fw file available

                    MessageCentre.OutputFirmwareUpdateMessage($"[{_targetName}] ERROR: failure to download package and couldn't find one in the cache.");

                    return false;
                }
            }

            // got here, must have a file!

            // unzip the firmware
            Console.Write($"Extracting {fwFileName}...");

            ZipFile.ExtractToDirectory(
                Path.Combine(LocationPath, fwFileName),
                LocationPath);

            // be nice to the user and delete any fw packages other than the last one
            var allFwFiles = Directory.EnumerateFiles(LocationPath, "*.zip").OrderByDescending(f => f).ToList();
            if(allFwFiles.Count > 1)
            {
                foreach (var file in allFwFiles.Skip(1))
                {
                    File.Delete(file);
                }
            }

            return true;
        }

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        // lets tidy up the disk and delete the fw files from disk
                        // wrap on a try/catch in case something goes wrong
                        if (!string.IsNullOrEmpty(LocationPath))
                        {
                            Directory.Delete(LocationPath, true);
                        }
                    }
                    catch
                    {
                        // don't care about exceptions where deleting folder
                        // can't do anything about it
                        // worst case is that the files will be hanging there until a disk clean-up occurs
                    }
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        void IDisposable.Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        #endregion

    }

}
