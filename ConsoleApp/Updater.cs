// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Threading.Tasks;
  using SharpCompress.Archives;
  
  /// <summary>
  /// Class to represent entity that performs update of local ffmpeg to latest stable version
  /// </summary>
  class Updater {
    // States from CLA
    private bool ShouldSimulate { get; set; }
    private string ffmpegLocation { get; set; }

    /// <summary>
    /// Constructor: sets first 5 properties
    /// <param name="ShouldSimulate"> whether to perform actual update </param>
    /// <param name="ffmpegLocation"> location of ffmpeg binary </param>
    /// </summary>
    public Updater(bool ShouldSimulate, string ffmpegLocation = @"D:\PFiles_x64\PT\ffmpeg") {
      this.ShouldSimulate = ShouldSimulate;
      this.ffmpegLocation = ffmpegLocation;
      // verify ffmpeg local binary Location
      if (!Directory.Exists(ffmpegLocation))
        throw new InvalidOperationException("Provided ffmpeg binary path not found!");
    }

    /// <summary>
    /// Download html page of a URL and extract the version from the page's radio button element
    /// <remarks>
    /// Demonstrates,
    /// - how to catch exception from async methods
    /// - how to download an html page using single call <c> GetStringAsync </c>
    /// HttpClient ref, https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient
    /// </remarks>
    /// </summary>
    private async Task<string> GetLatestFFMpegVersionAsync() {
      string version = string.Empty;
      HttpClient client = new HttpClient();
      // Call asynchronous network methods in a try/catch block to handle exceptions
      try {
        var windowsFFMpegBuildURL = "https://www.gyan.dev/ffmpeg/builds/#release-builds";
        string responseBody = string.Empty;
        // Add TLS v11 and v12
        // For Exception: Unable to read data from the transport connection: An existing connection
        // was forcibly closed by the remote host.
        // System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls11 |
        //   System.Net.SecurityProtocolType.Tls12;
        // do we need double try catch for catching exception from await? Confirm
        try {
          responseBody = await client.GetStringAsync(windowsFFMpegBuildURL);
        } catch (Exception e) {
          Console.WriteLine("Exception: " + e.Message);
          return version;
        }

        var needle = "latest release";
        int start = responseBody.IndexOf(needle);
        if (start == -1) {
          Console.WriteLine("Release build pattern string not found!" + responseBody);
          client.Dispose();
          // manual hack for now, since JS is blocked
          // return "4.3.1";
          return version;
        }
        needle = "version: <span id=\"release-version\">";

        if ((start = responseBody.IndexOf(needle, start + needle.Length)) == -1) {
          Console.WriteLine("Version placeholder string not found!");
          client.Dispose();
          return version;
        }
        start += needle.Length;
        var tail = "</span>";
        int end = responseBody.IndexOf(tail, start);
        if (end == -1) {
          Console.WriteLine("ending html tag after version string not found!");
          client.Dispose();
          return version;
        }
        version = responseBody.Substring(start, end - start);
      } catch (HttpRequestException e) {
        Console.WriteLine("Exception: " + e.Message);
        return version;
      }

      // Need to call dispose on the HttpClient object
      // when done using it, so the app doesn't leak resources
      client.Dispose();
      return version;
    }

    private string GetLocalFFMpegVersion() {
      var ffmpegBinPath = ffmpegLocation + @"\bin\ffmpeg.exe";
      if (File.Exists(ffmpegBinPath) == false) {
        Console.WriteLine("FFMpeg is not installed in specified location!");
        return "4.0.0"; // some old version string
      }

      string version = string.Empty;
      // Start the child process.
      var p = new System.Diagnostics.Process();
      // Redirect the output stream of the child process.
      p.StartInfo.FileName = ffmpegBinPath;
      p.StartInfo.Arguments = "-version";
      p.StartInfo.UseShellExecute = false;
      p.StartInfo.RedirectStandardOutput = true;
      p.Start();
      // reading to the end of its redirected stream.
      string output = p.StandardOutput.ReadToEnd();
      // Parse redirected output to find ffmpeg version
      var needle = "ffmpeg version ";  // local needle
      int start = output.IndexOf(needle);
      if (start == -1) {
        Console.WriteLine("ffmpeg version string not found!");
        return version;
      }
      start += needle.Length;
      // for zeranoe: ' Copyright'
      needle = "-full_build-www.gyan.dev";  // gyan.dev's builds
      int end = output.IndexOf(needle, start);
      if (end == -1) {
        Console.WriteLine("ffmpeg version string end not found!");
        return version;
      }
      version = output.Substring(start, end - start);
      // Do not wait for the child process to exit before
      // Read the output stream first and then wait.
      p.WaitForExit();
      p.Close();
      return version;
    }


    /// <summary>
    /// Extract 7zip Archives and clean up
    /// TODO: Needs an async version for this.. Fix breaking Async/Await
    /// </summary>
    /// <param name="filePath">the file path variable</param>
    /// <returns></returns>
    public bool Extract7zArchive(string filePath) {
      using (var archive = SharpCompress.Archives.SevenZip.SevenZipArchive.Open(filePath)) {
        // archive not null when next archive is not found 
        try {
          // ToDO: show warning for multiple files
          foreach (var entry in archive.Entries)
            entry.WriteToDirectory(Path.GetDirectoryName(filePath), new SharpCompress.Common.ExtractionOptions() {
              ExtractFullPath = true,
              Overwrite = true }
            );
        }
        catch (System.ArgumentException e) {
          Console.WriteLine("Probably could not find next archive! Msg:\r\n" + e.Message);
          return false;
        }
      }

      Console.WriteLine("Removing file: " + filePath);
      FileOperationAPIWrapper.Send(filePath);

      return true;
    }

    /// <summary>
    /// Download the binary and extract it into specified dir
    /// <remarks>
    /// net 7 web client deprecated last year
    /// HttpClient ref, https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient
    /// </remarks>
    /// </summary>

    private async Task UpdateLocalFFMpeg(string localVersion, string latestVersion) {
      Console.WriteLine("Updating..");
      var dirToDelete = ffmpegLocation + "." + localVersion;
      if (Directory.Exists(dirToDelete))
        FileOperationAPIWrapper.Send(dirToDelete);
      Console.WriteLine("Waiting for ffmpeg dir lock to be freed..");
      // 3s did not work on 02-15 for some reason, increased to 10s
      await Task.Delay(10000);
      Directory.Move(ffmpegLocation, dirToDelete);
      // example URL: 
      var ffmpegURL = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-" + latestVersion + "-full_build-shared.7z";
      // download and extract latest ffmpeg
      HttpClient client = new HttpClient();
      // check later when getting exception about forcibly closed connetion on transport
      // System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls11 |
      //  System.Net.SecurityProtocolType.Tls12;

      string newffmpegDirName = ffmpegLocation + "-" + latestVersion + "-full_build-shared";
      string archiveFileNameWithExt = newffmpegDirName + ".7z";
      Console.WriteLine("Downloading " + ffmpegURL);
      Console.WriteLine("Temporarily saving to " + archiveFileNameWithExt);

      // Send asynchronous request
      try {
        await client.GetAsync(ffmpegURL).ContinueWith(
          (requestTask) => {
          // Get HTTP response from completed task.
          HttpResponseMessage response = requestTask.Result;
          // Check that response was successful or throw exception
          response.EnsureSuccessStatusCode();
          // Read response asynchronously and save to file
          response.Content.ReadAsFileAsync(archiveFileNameWithExt, true);
          });
      } catch (Exception e) { 
        Console.WriteLine("Exception: " + e.Message);
        return ;
      }
      // wait for file stream to release the file
      await Task.Delay(1000);
      Extract7zArchive(archiveFileNameWithExt);
      // System.IO.Compression.ZipFile.ExtractToDirectory(zipFileNameWithExt, ffmpegLocation + @"\..\");
      Directory.Move(newffmpegDirName, ffmpegLocation);
      FileOperationAPIWrapper.Send(dirToDelete);
      FileOperationAPIWrapper.Send(archiveFileNameWithExt);
    }

    /// <summary>
    /// Compare local and online ffmpeg versions, retrieve these info asynchronously. Based on
    /// comparison result initiate an update asynchronously
    /// <c> versionStringMaxLength </c> is 7 for stable version. To support beta release parsing it
    /// is currently set to 20. ref, https://ffmpeg.zeranoe.com/builds/
    /// In future: may be support update to beta version
    /// </summary>
    public async Task UpdateFFMpegAsync() {
      const int versionStringMaxLength = 20;
      var ffmpegGetOnlineStringTask = Task.Run(() => GetLatestFFMpegVersionAsync());
      // at this point, GetLatestFFMpegVersion and the local version retrieval run concurrently
      var localVersion = GetLocalFFMpegVersion();
      // let's wait till GetLatestFFMpegVersion finishes
      var latestVersion = await ffmpegGetOnlineStringTask;
      if (localVersion == string.Empty || latestVersion == string.Empty || localVersion.Length >
        versionStringMaxLength || latestVersion.Length > versionStringMaxLength) {
        Console.WriteLine("Error while retrieving version information!");
        return;
      }
      Console.WriteLine("Local version: " + localVersion);
      Console.WriteLine("Latest stable found online: " + latestVersion);
      if (localVersion != latestVersion) {
        if (ShouldSimulate) {
          Console.WriteLine("Remove simulate flag to update");
          return;
        }
        await UpdateLocalFFMpeg(localVersion, latestVersion);
      }
    }

  }

  // Utility Class FileOperationAPIWrapper is in its own file and Class Extensions is below

  /// <summary> Provides an async method extension to GetAsync so the content can be saved in a file
  /// Application. <see cref="UpdateLocalFFMpeg"/>
  /// ref, blogs.msdn.microsoft.com/henrikn/2012/02/17/httpclient-downloading-to-a-local-file/ 
  /// </summary>
  public static class HttpContentExtensions {
    public static Task ReadAsFileAsync(this HttpContent content, string filename, bool overwrite) {
      string pathname = Path.GetFullPath(filename);
      if (!overwrite && File.Exists(filename)) {
        throw new InvalidOperationException(string.Format("File {0} already exists.", pathname));
      }

      FileStream? fileStream = null;
      try {
        fileStream = new FileStream(pathname, FileMode.Create, FileAccess.Write, FileShare.None);
        return content.CopyToAsync(fileStream).ContinueWith(
            (copyTask) => {
              fileStream.Close();
            });
      } catch {
        if (fileStream != null) {
          fileStream.Close();
        }

        throw;
      }
    }
  }
}
