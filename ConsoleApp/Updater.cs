// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Threading.Tasks;
  
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
    /// </remarks>
    /// </summary>
    private async Task<string> GetLatestFFMpegVersionAsync() {
      string version = string.Empty;
      HttpClient client = new HttpClient();
      // Call asynchronous network methods in a try/catch block to handle exceptions
      try {
        var windowsFFMpegBuildUri = new Uri("https://ffmpeg.zeranoe.com/builds/");
        string responseBody = string.Empty;
        // For Exception: Unable to read data from the transport connection: An existing connection
        // was forcibly closed by the remote host.
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls11 |
          System.Net.SecurityProtocolType.Tls12;
        // do we need double try catch for catching exception from await? Confirm
        try {
          responseBody = await client.GetStringAsync(windowsFFMpegBuildUri);
        } catch (Exception e) {
          Console.WriteLine("Exception: " + e.Message);
          return version;
        }

        var needle = "Release builds are recommended for distributors";
        int start = responseBody.IndexOf(needle);
        if (start == -1) {
          Console.WriteLine("Release build pattern string not found!" + responseBody);
          client.Dispose();
          // manual hack for now, since JS is blocked
          // return "4.3.1";
          return version;
        }
        needle = "name=\"v\" value=\"";

        if ((start = responseBody.IndexOf(needle, start + needle.Length)) == -1) {
          Console.WriteLine("Version placeholder string not found!");
          client.Dispose();
          return version;
        }
        start += needle.Length;
        int end = responseBody.IndexOf('"', start);
        if (end == -1) {
          Console.WriteLine("ending quote for version not found!");
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
      string version = string.Empty;
      // Start the child process.
      var p = new System.Diagnostics.Process();
      // Redirect the output stream of the child process.
      p.StartInfo.FileName = ffmpegLocation + @"\bin\ffmpeg.exe";
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
      needle = " Copyright";  // local needle
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
      var ffmpegURL = "https://ffmpeg.zeranoe.com/builds/win64/shared/ffmpeg-" + latestVersion + "-win64-shared.zip";
      // download and extract latest ffmpeg
      HttpClient client = new HttpClient();
      // check later when getting exception about forcibly closed connetion on transport
      // System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls11 |
      //  System.Net.SecurityProtocolType.Tls12;

      string newffmpegDirName = ffmpegLocation + "-" + latestVersion + "-win64-shared";
      string zipFileNameWithExt = newffmpegDirName + ".zip";
      Console.WriteLine("Downloading " + ffmpegURL);
      Console.WriteLine("Temporarily saving to " + zipFileNameWithExt);
      // Send asynchronous request
      try {
        await client.GetAsync(ffmpegURL).ContinueWith(
          (requestTask) => {
          // Get HTTP response from completed task.
          HttpResponseMessage response = requestTask.Result;
          // Check that response was successful or throw exception
          response.EnsureSuccessStatusCode();
          // Read response asynchronously and save to file
          response.Content.ReadAsFileAsync(zipFileNameWithExt, true);
          });
      } catch (Exception e) {
        Console.WriteLine("Exception: " + e.Message);
        return ;
      }
      // wait for file stream to release the file
      await Task.Delay(1000);
      System.IO.Compression.ZipFile.ExtractToDirectory(zipFileNameWithExt, ffmpegLocation + @"\..\");
      Directory.Move(newffmpegDirName, ffmpegLocation);
      FileOperationAPIWrapper.Send(dirToDelete);
      FileOperationAPIWrapper.Send(zipFileNameWithExt);
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

      FileStream fileStream = null;
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
