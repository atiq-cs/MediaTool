// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.Net.Http;
  using System.Threading.Tasks;

  /// <summary>
  /// Class to represent entity that performs update of local ffmpeg to latest stable version
  /// </summary>
  class Updater {
    // States from CLA
    private bool ShouldSimulate { get; set; }
    private string ffmpegLocation;

    /// <summary>
    /// Constructor: sets first 5 properties
    /// </summary>
    public Updater(bool ShouldSimulate, string ffmpegLocation = @"D:\PFiles_x64\PT\ffmpeg") {
      this.ShouldSimulate = ShouldSimulate;
      this.ffmpegLocation = ffmpegLocation;
      // verify ffmpeg local binary Location
      if (!System.IO.Directory.Exists(ffmpegLocation))
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
        try {
          responseBody = await client.GetStringAsync(windowsFFMpegBuildUri);
        } catch (Exception e) {
          Console.WriteLine("Exception: " + e.Message);
          return version;
        }

        var needle = "Release builds are recommended for distributors";
        int start = responseBody.IndexOf(needle);
        if (start == -1) {
          Console.WriteLine("Release build pattern string not found!");
          client.Dispose();
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

    private void UpdateLocalFFMpeg(string localVersion, string latestVersion) {
      Console.WriteLine("Updating..");
      var dirToDelete = ffmpegLocation + "." + localVersion;
      System.IO.Directory.Move(ffmpegLocation, dirToDelete);
      // download and extract latest ffmpeg
      System.IO.Directory.Delete(dirToDelete);
    }


    /// <summary>
    /// Update ffmpeg
    /// Future: may be support update to beta version
    /// </summary>
    public async Task UpdateFFMpegAsync() {
      const int versionStringMaxLength = 5;
      var ffmpegGetOnlineStringTask = Task.Run(() => GetLatestFFMpegVersionAsync());
      // at this point, runs fibo and timer concurrently
      var localVersion = GetLocalFFMpegVersion();
      // now let's wait till timer finishes
      var latestVersion = await ffmpegGetOnlineStringTask;
      if (localVersion == string.Empty || latestVersion == string.Empty || localVersion.Length >
        versionStringMaxLength || latestVersion.Length > versionStringMaxLength) {
        Console.WriteLine("Error while retrieving version information!");
        return ;
      }
      Console.WriteLine("Local version: " + localVersion);
      Console.WriteLine("Latest stable found online: " + latestVersion);
      if (localVersion != latestVersion) {
        if (ShouldSimulate) {
          Console.WriteLine("Remove simulate flag to update");
          return ;
        }
        UpdateLocalFFMpeg(localVersion, latestVersion);
      }
    }
  }
}
