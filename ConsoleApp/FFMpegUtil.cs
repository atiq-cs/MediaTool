// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

using System;
using System.Threading.Tasks;

namespace ConsoleApp {
  internal class FFMpegUtil {
    /// <summary>
    /// Props/methods related to single file processing
    /// <remarks>
    /// Member variable probably cannot be reference
    /// A solution is to pass this to methods
    /// </remarks>
    /// </summary>

    public FFMpegUtil(ref FileInfoType fileInfo) {
      FFMpegPath = @"D:\PFiles_x64\PT\ffmpeg";
      this.mFileInfo = fileInfo;
      ShouldChangeContainer = true;
    }

    private FileInfoType mFileInfo;
    private string FFMpegPath { get; set; }
    private bool ShouldChangeContainer;

    internal async Task Run(bool ShouldSimulate = true)
    {
      var filePath = mFileInfo.Path;
      // var mediaInfo = FileInfo.Parent + @"\ffmpeg_media_info.log";
      Console.WriteLine("Processing Media " + filePath + ":");

      string ffprobeStr = await GetMediaInfo(filePath);
      var sCodecId = ParseMediaInfo(ffprobeStr);

      if (ShouldSimulate)
        return;

      if (!string.IsNullOrEmpty(sCodecId))
        await ExtractSubtitle(filePath, sCodecId);

      if (ShouldChangeContainer && ! mFileInfo.ModInfo.Contains("Fail")) {
        // ConvertMedia has a high elapsed time, take advantage of async: do tasks before await inside
        bool isSuccess = await ConvertMedia(filePath);
        if (isSuccess) {
          // remove the input mkv file
          FileOperationAPIWrapper.Send(filePath);

          // update file path
          mFileInfo.Path = mFileInfo.Parent + "\\" +
            System.IO.Path.GetFileNameWithoutExtension(filePath) + ".mp4";
          mFileInfo.SetDirtyFlag("convert");

          if (!ShouldSimulate && !mFileInfo.ModInfo.Contains("Fail")) { 
            // show how output looks like
            Console.WriteLine();
            ffprobeStr = await GetMediaInfo(mFileInfo.Path);
            sCodecId = ParseMediaInfo(ffprobeStr);
            System.Diagnostics.Debug.Assert(string.IsNullOrEmpty(sCodecId));
          }
        }
      }
    }

    private async Task<string> GetMediaInfo(string filePath) {
      string ffprobeStr = string.Empty;

      using (System.Diagnostics.Process probeProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = FFMpegPath + @"\bin\ffprobe.exe",
          Arguments = "-i \""+ filePath + "\"",
          UseShellExecute = false,
          RedirectStandardError = true
        },
        EnableRaisingEvents = true }
      ) {
        try {
          TaskCompletionSource<bool> probeEventHandled = new TaskCompletionSource<bool>();

          // Start a process and raise an event when done.
          // accessing variables inside this event i.e., ffprobeStr or ReadToEnd will create deadlock
          probeProcess.Exited += (sender, args) => {
            if (probeProcess.ExitCode != 0) {
              Console.WriteLine("Exit code: " + probeProcess.ExitCode + ", please check if it's corrupted file: " + filePath);
              // ToDo: pass FileInfo ** high pri this one
              mFileInfo.SetDirtyFlag("Fail: corrupted media file");
            }

            Console.WriteLine("Elapsed time: " + Math.Round((probeProcess.ExitTime - probeProcess.
              StartTime).TotalMilliseconds) + " ms");
            // $"Exit time    : {probeProcess.ExitTime}, " +

            probeEventHandled.TrySetResult(true);
            probeProcess.Dispose();
          };

          probeProcess.Start();
          ffprobeStr = probeProcess.StandardError.ReadToEnd();
          // Blocking start process example
          // probeProcess.WaitForExit();
          // Console.WriteLine("Received output: " + ffprobeStr);

          // Wait for ffProbe process Exited event, but not more than 10 seconds
          await Task.WhenAny(probeEventHandled.Task, Task.Delay(10000));
        }
        catch (Exception ex) {
          Console.WriteLine($"An error occurred trying to run ffmpeg probe \"{FFMpegPath}\":\n{ex.Message}");
          return ffprobeStr;
        }

        if (string.IsNullOrEmpty(ffprobeStr)) {
          probeProcess.Dispose();
          throw new InvalidOperationException("Media Info (probe) is empty for provided media.");
        }
      }
      return ffprobeStr;
    }

    private string ParseMediaInfo(string mediaInfoStr) {
      string sCodeId = string.Empty;

      if (string.IsNullOrEmpty(mediaInfoStr))
        return sCodeId;

      var needle = "Input #0";
      int start = mediaInfoStr.IndexOf(needle);

      if (start == -1) {
        Console.WriteLine("Container info not found, corrupted file?");
        return sCodeId;
      }

      int prevNeedlePos = start + needle.Length;
      needle = "from '";

      if ((start = mediaInfoStr.IndexOf(needle, prevNeedlePos)) == -1) {
        Console.WriteLine("Container info end needle not found!!");
        return sCodeId;
      }
      Console.WriteLine("Container: " + mediaInfoStr.Substring(prevNeedlePos+2, start -
        prevNeedlePos-4));

      // assuming container info wouldn't be less than 50, usually filled with metadata and chapter info
      prevNeedlePos = start + needle.Length + 50;
      var lineNeedle = "Stream #0:";
      var lineEndNeedle = "Metadata:";
      var langTitleNeedle = "title";
      var streamEndNeedle = "_STATISTICS_WRITING_DATE";
      Console.WriteLine("Streams found: ");

      int aCount = 0, sCount = 0;

      while ((start = mediaInfoStr.IndexOf(lineNeedle, prevNeedlePos)) != -1) {
        prevNeedlePos = start + lineNeedle.Length;

        if ((start = mediaInfoStr.IndexOf(lineEndNeedle, prevNeedlePos + 2)) == -1) {
          Console.WriteLine("Stream info line end needle not found!");
          return sCodeId;
        }

        var streamLine = mediaInfoStr.Substring(prevNeedlePos - 2, start - prevNeedlePos - 4);
        Console.WriteLine(" " + streamLine);
        prevNeedlePos = start + lineEndNeedle.Length;

        // will throw IndexOutOfRangeException if result does not have 2
        var streamType = streamLine.Split(": ")[1];
        if (string.IsNullOrEmpty(streamLine) || string.IsNullOrEmpty(streamType)) {
          mFileInfo.SetDirtyFlag("Fail: unexpected input found");
          return sCodeId;
        }

        switch (streamType) {
          case "Subtitle":
            // set s codec id
            // (eng) ... (default)
            if (streamLine.Contains("(eng)") && streamLine.Contains("(default)"))
              sCodeId = "0:" + streamLine.Split(new Char[] { ':', '(' })[1];
            // (eng)
            else if (streamLine.Contains("(eng)") && string.IsNullOrEmpty(sCodeId))
              sCodeId = "0:" + streamLine.Split(new Char[] { ':', '(' })[1];

            // Metadata (Title, BPS etc) info show only Title for verbosity
            if ((start = mediaInfoStr.IndexOf(streamEndNeedle, prevNeedlePos + 2)) == -1)
              Console.WriteLine("Stream info end needle not found!");
            else {
              Console.WriteLine(mediaInfoStr.Substring(prevNeedlePos + 2, start - prevNeedlePos - 10));
              prevNeedlePos = start + streamEndNeedle.Length;
            }
            sCount++;
            break;

          case "Audio":
            aCount++;
            break;
          case "Video":
            break;
          case "Data":
            Console.WriteLine("Stream Data found: is input an mp4 file?");
            break;
          default:
            // Todo
            mFileInfo.SetDirtyFlag("Fail: unknown stream found");
            break;
        }
      }

      Console.WriteLine("Nmber of subtitles: " + sCount + ", nmber of audio: " + aCount);

      if (sCount > 0) {
        if (string.IsNullOrEmpty(sCodeId)) {
          Console.WriteLine("Failed to choose subtitle index! Disabling container change..");
          ShouldChangeContainer = false;
        }
        else {
          Console.WriteLine("Subtile stream index: " + sCodeId);
      }

      if (aCount > 1) {
        // show warning, parse aCodec Id
        Console.WriteLine("Audio streams# " + aCount + "! Disabling container change..");
        ShouldChangeContainer = false;
        }
      }

      return sCodeId;
    }


    private async Task ExtractSubtitle(string filePath, string sCodecId) {
      // mkv, mp4, mpeg-2 m2ts, mov, qt can contain subtitles as attachments
      // ref, https://en.wikipedia.org/wiki/Comparison_of_video_container_formats
      // for now, for psa and rmz we only accept mkv
      if (System.IO.Path.GetExtension(filePath).ToLower() != ".mkv")
        return;

      // https://docs.microsoft.com/en-us/dotnet/api/system.io.path.getfilenamewithoutextension
      var srtFilePath = mFileInfo.Parent + "\\" + System.IO.Path.GetFileNameWithoutExtension(filePath)
        + ".srt";
      TaskCompletionSource<bool> ffmpegEventHandled = new TaskCompletionSource<bool>();

      using (System.Diagnostics.Process ffmpegProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = FFMpegPath + @"\bin\ffmpeg.exe",
          // -y overwrite output file if exists
          Arguments = " -loglevel fatal -i \""+ filePath + "\"" + " -codec:s srt -map " +
            sCodecId + " \"" + srtFilePath + "\"",
          UseShellExecute = false
        },
        EnableRaisingEvents = true }
      ) {
        try {
          // Start a process and raise an event when done.
          ffmpegProcess.Exited += (sender, args) => {
            if (ffmpegProcess.ExitCode != 0) {
              Console.WriteLine("Exit code: " + ffmpegProcess.ExitCode + ", an overwrite is not" +
                "confirmed or ffmpeg is invoked incorrectly! Please check input stream. codec id: " + sCodecId);
            }

            Console.Write("Subtitle extraction time: " + Math.Round((ffmpegProcess.ExitTime - ffmpegProcess.
              StartTime).TotalMilliseconds) + " ms (), ");

            ffmpegEventHandled.TrySetResult(true);
            ffmpegProcess.Dispose();
          };

          ffmpegProcess.Start();
          // better to utilize onExit than `WaitForExit`
        }
        catch (Exception ex) {
          Console.WriteLine($"An error occurred trying to run ffmpeg scodec copy \"{FFMpegPath}\"");
          Console.WriteLine(ex.Message);
          return;
        }

        // Run Concurrent
        // Cleanup garbage sub
        // Change sponsor text in psarip subs

        // Wait for ffmpeg process Exited event, but not more than 120 seconds
        await Task.WhenAny(ffmpegEventHandled.Task, Task.Delay(120000));
      }

      // after extracting if it results a garbage subtitle (with sCount > 1) set shouldChangeContainer to false
      long srtSize = (new System.IO.FileInfo(srtFilePath)).Length;
      Console.WriteLine("Srt size: {0:F2} KB", srtSize * 1.0 / 1024);

      // Expecting at least 5 KB; if found less notify, but don't affect cont. change
      if (srtSize < (5 * 1024)) {
        Console.WriteLine("Subtitle file size is small (< 5 KB)!");
        // ShouldChangeContainer = false;
      }
    }

    private async Task<bool> ConvertMedia(string filePath) {
      // for now, only convert mkv
      if (System.IO.Path.GetExtension(filePath).ToLower() != ".mkv")
        return false;

      // https://docs.microsoft.com/en-us/dotnet/api/system.io.path.getfilenamewithoutextension
      var mpegFilePath = mFileInfo.Parent + "\\" + System.IO.Path.GetFileNameWithoutExtension(filePath)
        + ".mp4";
      TaskCompletionSource<bool> ffmpegEventHandled = new TaskCompletionSource<bool>();

      using (System.Diagnostics.Process ffmpegProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = FFMpegPath + @"\bin\ffmpeg.exe",
          // ffmpeg remove menu chapters
          // https://video.stackexchange.com/questions/20270/ffmpeg-delete-chapters
          Arguments = " -loglevel fatal -i \""+ filePath + "\"" + " -sn -map_chapters -1 -codec:v copy -codec:a "
            + "copy \"" + mpegFilePath + "\"",
          UseShellExecute = false
        },
        EnableRaisingEvents = true }
      ) {
        try {
          // Start a process and raise an event when done.
          ffmpegProcess.Exited += (sender, args) => {
            if (ffmpegProcess.ExitCode != 0) {
              Console.WriteLine("Exit code: " + ffmpegProcess.ExitCode + ", ffmpeg is invoked " +
                "incorrectly! Please check input stream. args: " + ffmpegProcess.StartInfo.Arguments);
              mFileInfo.SetDirtyFlag("Fail: media conversion");
              return;
            }

            Console.WriteLine("Elapsed time: " + Math.Round((ffmpegProcess.ExitTime - ffmpegProcess.
              StartTime).TotalMilliseconds) + " ms (change to mp4 container");

            ffmpegEventHandled.TrySetResult(true);
            ffmpegProcess.Dispose();
          };

          ffmpegProcess.Start();
        }
        catch (Exception ex) {
          Console.WriteLine($"An error occurred trying to run ffmpeg scodec copy \"{FFMpegPath}\"");
          Console.WriteLine(ex.Message);
          return false;
        }

        long inSize = (new System.IO.FileInfo(filePath)).Length;

        // Wait for ffmpeg process Exited event, but not more than 40 seconds
        await Task.WhenAny(ffmpegEventHandled.Task, Task.Delay(40000));

        // after extracting if it results a small file < 50 MB of original
        long mpegSize = (new System.IO.FileInfo(mpegFilePath)).Length;

        if ((inSize - mpegSize) > (50 * 1024 * 1024))
          return false;
      }

      return true;
    }
  }
}