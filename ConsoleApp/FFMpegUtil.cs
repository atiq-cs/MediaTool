// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

using System;
using System.Threading.Tasks;
using System.Text.Json;
using FFMpeg;


namespace ConsoleApp {
  internal class FFMpegUtil {
    /// <summary>
    /// Props/methods related to single file processing
    /// </summary>
    /// <remarks>
    /// Member variable probably cannot be reference
    /// A solution is to pass this to methods
    /// <c>code block inline example</c>
    ///
    /// Later, may be verify if found block contains property as well.
    /// Don't pass `filePath` as param, it is updated by rename and should be retrieved from FileInfo
    /// https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute
    /// </remarks>
    public FFMpegUtil(ref FileInfoType fileInfo) {
      FFMpegPath = @"C:\PFiles_x64\PT\ffmpeg";
      this.mFileInfo = fileInfo;
      ShouldChangeContainer = true;
    }

    private FileInfoType mFileInfo;
    private string FFMpegPath { get; set; }
    private bool ShouldChangeContainer;

    /// <summary>
    /// Since an async method does not support ref parameter passing we cannot utilize
    /// `ref FileInfoType` here. Hence we copy it back in the end.
    /// </summary>
    /// <param name="ShouldSimulate"></param>
    /// <returns></returns>
    internal async Task<FileInfoType> Run(bool ShouldSimulate = true) {
      var filePath = mFileInfo.Path;
      // var mediaInfo = FileInfo.Parent + @"\ffmpeg_media_info.log";
      Console.WriteLine("Processing Media " + filePath + ":");

      string ffprobeStr = await GetMediaInfo(filePath);
      // TODO: make this debug statements more compact
      // Console.WriteLine("json string:" + ffprobeStr);
      var codecId = ParseMediaInfo(ffprobeStr);

      var codes = codecId.Split(' ');
      if (codes.Length == 0)
        throw new ArgumentException("codecs are not set!");

      var sCodecId = codes[0];
      var aCodecId = codes[1];

      Console.WriteLine("a code: " + aCodecId + ", s code: " + (string.IsNullOrEmpty(sCodecId)?
        "Empty": sCodecId));

      if (ShouldSimulate)
        return mFileInfo;

      if (!string.IsNullOrEmpty(sCodecId))
        await ExtractSubtitle(filePath, sCodecId);

      if (ShouldChangeContainer && ! mFileInfo.IsInError) {
        // ToDo: check for space ahead? to avoid reanme in case of not free space
        // ConvertMedia has a high elapsed time, take advantage of async: do tasks before await
        // inside
        bool isSuccess = await ConvertMedia(filePath, aCodecId);
        if (isSuccess) {
          mFileInfo.Update("convert");

          // remove the input mkv file
          FileOperationAPIWrapper.Send(filePath);

          // update file path
          mFileInfo.Path = mFileInfo.Parent + "\\" +
            System.IO.Path.GetFileNameWithoutExtension(filePath) + ".mp4";

          if (!ShouldSimulate && !mFileInfo.IsInError) { 
            // show how output media looks like
            Console.WriteLine();
            ffprobeStr = await GetMediaInfo(mFileInfo.Path);
            sCodecId = ParseMediaInfo(ffprobeStr);
            // sCodecId can be null if there's no sub
            // System.Diagnostics.Debug.Assert(string.IsNullOrEmpty(sCodecId));
          }
        }
      }
      return mFileInfo;
    }

    /// <summary>
    /// Given media file return media info string (json formatted str)
    /// 
    ///
    /// 
    /// ##Refs
    /// - Using ffmpeg to get video info - why do I need to specify an output file?
    ///  https://stackoverflow.com/q/11400248
    /// - Get ffmpeg information in friendly way
    ///  https://stackoverflow.com/q/7708373
    ///
    /// </summary>
    /// <remarks>
    /// `-loglevel quiet` or `-v quiet` suppresses all error in standard error
    /// after using json args all outputs are in std out instead of stdin
    ///  I don't know why I was reading from std err previously, I should had figured out std err
    //  so if there's any error ffmpeg, it will probably still show up?
    /// Instead of `-loglevel quiet` I utitlize `-loglevel warning`
    ///
    /// Ref,
    ///   https://ffmpeg.org/ffmpeg.html
    /// </remarks>
    /// <param name="filePath">input media file</param>
    /// <returns></returns>
    private async Task<string> GetMediaInfo(string filePath) {
      string ffprobeStr = string.Empty;

      using (System.Diagnostics.Process probeProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = FFMpegPath + @"\bin\ffprobe.exe",
          Arguments = "-loglevel warning -print_format json -show_format -show_streams -i \""+ filePath + "\"",
          UseShellExecute = false,
          RedirectStandardOutput = true
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
              mFileInfo.Update("Fail: corrupted media file");
            }

            Console.WriteLine("GetMediaInfo() took " + Math.Round((probeProcess.ExitTime - probeProcess.
              StartTime).TotalMilliseconds) + " ms");
            // $"Exit time    : {probeProcess.ExitTime}, " +

            probeEventHandled.TrySetResult(true);
            probeProcess.Dispose();
          };

          probeProcess.Start();
          ffprobeStr = probeProcess.StandardOutput.ReadToEnd();
          // Blocking start process example
          // probeProcess.WaitForExit();

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

    /// <summary>
    /// Parse json media info string
    /// </summary>
    /// <remarks>
    /// json writer for ffmpeg/ffprobe
    ///   FFmpeg/blob/master/fftools/ffprobe.c
    /// </remarks>
    /// <param name="mediaInfoJson">Given json formatted media info string parse subtitle info</param>
    /// <returns></returns>
    private string ParseMediaInfo(string mediaInfoJson) {
      string sCodeId = string.Empty;

      if (string.IsNullOrEmpty(mediaInfoJson))
        return sCodeId;
      var deserializeOptions = new JsonSerializerOptions{PropertyNamingPolicy =
        JsonNamingPolicy.CamelCase};
      deserializeOptions.Converters.Add(new IntConverter());
      deserializeOptions.Converters.Add(new LongConverter());
      deserializeOptions.Converters.Add(new FloatConverter());
      deserializeOptions.Converters.Add(new BoolConverter());

      var probeObj = System.Text.Json.JsonSerializer.Deserialize<FFProbeType>(mediaInfoJson,
        deserializeOptions);

      int aCount = 0, sCount = 0;
      string initialACodecId = string.Empty;
      string initialSCodecId = string.Empty;
      string aCodeId = string.Empty;

      if (probeObj.Streams == null)
        throw new InvalidOperationException("Possible bad input file!");

      foreach(var stream in probeObj.Streams) {
        string CodecType = stream.codec_type;
        Console.WriteLine($"index: {stream.index}, codec: {stream.codec_name} lang: " + (
          stream.tags != null && stream.tags.ContainsKey("language")?stream.tags["language"]:"null"));

        switch (CodecType) {
          case "subtitle":
            if (stream.codec_name != "subrip" && stream.codec_name != "ass") {
              Console.WriteLine("Ignoring unsupported sub format: " + stream.codec_name);
              break;
            }

            // (eng)
            if (stream.tags.ContainsKey("language") && stream.tags["language"] == "eng" && string.
                IsNullOrEmpty(sCodeId))
              sCodeId = "0:" + stream.index.ToString();
            else if (string.IsNullOrEmpty(initialSCodecId)) {
              Console.WriteLine("defaulting to initial s index");
              initialSCodecId = "0:" + stream.index.ToString();
            }

            // Discovery; after enough data remove this
            switch (mFileInfo.Ripper) {
            case "psa":
              if (stream.codec_name == "ass")
                Console.WriteLine("(SSA sub found: this is new for " + mFileInfo.Ripper + ")");
              break;

            // RMTeam
            case "RMT":
              if (stream.codec_name == "subrip")
                Console.WriteLine("(Subrip found: this is new for " + mFileInfo.Ripper + ")");
              if (stream.tags.ContainsKey("title"))
                Console.WriteLine("(Title found: this is new for " + mFileInfo.Ripper + ")");
              break;

            case "HET":
              if (stream.codec_name == "hdmv_pgs_subtitle")
                Console.WriteLine("HET hdmv_pgs_subtitle skipping..");
              else if (stream.codec_name == "dvd_subtitle")
                Console.WriteLine("HET dvd_subtitle skipping..");
              else if (string.IsNullOrEmpty(sCodeId)) {
                sCodeId = "0:" + stream.index.ToString();
              }
              else
                Console.WriteLine("(eng sub found: this is new for " + mFileInfo.Ripper + ")");

              if (stream.codec_name == "ass")
                Console.WriteLine("(SSA sub found: this is new for " + mFileInfo.Ripper + ")");
              break;

            default:
              break;
            }

            sCount++;
            break;

          case "audio":
            // show warning for non- 'eng' Audio i.e., und, rus, dan, kor, pol, chi
            // eac3 found with HET, x264 video
            if (stream.codec_name != "aac" && stream.codec_name != "ac3" && stream.codec_name != "eac3" && stream.codec_name != "mp3" && stream.codec_name != "vorbis") {
              Console.WriteLine($"Unsupported audio: {stream.codec_name}!");
              break;
            }

            // (eng) ... (default) Or (eng)
            if (stream.tags.ContainsKey("language") && stream.tags["language"] == "eng" && string.IsNullOrEmpty(aCodeId))
              aCodeId = "0:" + stream.index.ToString();
            else if (string.IsNullOrEmpty(initialACodecId))
              initialACodecId = "0:" + stream.index.ToString();
            aCount++;
            break;
          case "video":
            break;
          case "data":
            Console.WriteLine("Stream 'data' found! (not good if it's in output mp4 file)");
            break;
          default:
            // Todo
            mFileInfo.Update($"Fail: unknown codec type {CodecType} found");
            break;
        }
      }

      Console.WriteLine("Number of supported subtitles: " + sCount + ", number of supported audio: " + aCount);

      if (sCount > 0) {
        if (string.IsNullOrEmpty(sCodeId)) {
          Console.WriteLine("Could not find EN subtitle in input!");
          // ShouldChangeContainer = false;

          if (sCount == 1) {
            switch (mFileInfo.Ripper) {
            case "psa":
              Console.WriteLine("this is unusual for " + mFileInfo.Ripper + "!");
              break;
            }
          }

          sCodeId = initialSCodecId;
          Console.WriteLine("Defaulted to only found subtitle with index: " + sCodeId);
        }
        else
          Console.WriteLine("Subtitle stream index: " + sCodeId);
        System.Diagnostics.Debug.Assert(! string.IsNullOrEmpty(sCodeId));
      }

      if (string.IsNullOrEmpty(aCodeId)) {
        if (aCount == 1) {
          aCodeId = initialACodecId;
          Console.WriteLine("Audio stream index: " + aCodeId + "; (eng) not found in audio metadata");
        }
        else if (aCount > 1) {
          // enabled last time with hindi movie
          Console.WriteLine("Warning: Audio streams# " + aCount + ", (eng) audio not found! Disabling container change.. Defaulting..");
          // previously we used to show error and stop
          // mFileInfo.Update("Fail: Audio streams# " + aCount + ", (eng) audio not found! Disabling container change..");
          aCodeId = initialACodecId;
          ShouldChangeContainer = false;
        }
      }
      else
        Console.WriteLine("Audio stream index: " + aCodeId);

      System.Diagnostics.Debug.Assert(ShouldChangeContainer && ! string.IsNullOrEmpty(aCodeId), "No supported audio found!");
      return (sCodeId + ' ' + aCodeId);
    }


    private async Task ExtractSubtitle(string filePath, string sCodecId) {
      // mkv, mp4, mpeg-2 m2ts, mov, qt can contain subtitles as attachments
      // ref, https://en.wikipedia.org/wiki/Comparison_of_video_container_formats
      // for now, for psa and rmz we only accept mkv
      if (System.IO.Path.GetExtension(filePath).ToLower() != ".mkv")
        return;

      // https://docs.microsoft.com/en-us/dotnet/api/system.io.path.getfilenamewithoutextension
      var subRipExt = ".srt";
      var srtFilePath = mFileInfo.Parent + "\\" + System.IO.Path.GetFileNameWithoutExtension(filePath)
        + subRipExt;

      if (System.IO.File.Exists(srtFilePath)) {
        Console.WriteLine("Subrip file already exists, renaming");
        var oldSrtFilePath = srtFilePath.Substring(0, srtFilePath.Length-subRipExt.Length) + "_old.srt";

        if (System.IO.File.Exists(oldSrtFilePath))
          FileOperationAPIWrapper.Send(oldSrtFilePath);

        System.IO.File.Move(srtFilePath, oldSrtFilePath);
      }

      TaskCompletionSource<bool> ffmpegEventHandled = new TaskCompletionSource<bool>();

      using (System.Diagnostics.Process ffmpegProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = FFMpegPath + @"\bin\ffmpeg.exe",
          // -y overwrite output file if exists
          Arguments = " -loglevel warning -i \""+ filePath + "\"" + " -codec:s srt -map " +
            sCodecId + " \"" + srtFilePath + "\"",
          UseShellExecute = false
        },
        EnableRaisingEvents = true }
      ) {
        try {
          // Start a process and raise an event when done.
          ffmpegProcess.Exited += (sender, args) => {
            if (ffmpegProcess.ExitCode != 0) {
              Console.WriteLine("Exit code: " + ffmpegProcess.ExitCode + ", an overwrite is not " +
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

    private async Task<bool> ConvertMedia(string filePath, string aCodecId) {
      // codec id required for audio
      if (string.IsNullOrEmpty(aCodecId))
        return false;

      // for now, only convert mkv
      if (System.IO.Path.GetExtension(filePath).ToLower() != ".mkv")
        return false;

      // https://docs.microsoft.com/en-us/dotnet/api/system.io.path.getfilenamewithoutextension
      var mpegFilePath = mFileInfo.Parent + "\\" + System.IO.Path.GetFileNameWithoutExtension(filePath)
        + ".mp4";

      if (System.IO.File.Exists(mpegFilePath)) {
        Console.WriteLine("mp4 file already exists!");
        mFileInfo.IsInError = true;
        return false;
      }

      TaskCompletionSource<bool> ffmpegEventHandled = new TaskCompletionSource<bool>();

      using (System.Diagnostics.Process ffmpegProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = FFMpegPath + @"\bin\ffmpeg.exe",
          // ffmpeg remove menu chapters
          // https://video.stackexchange.com/questions/20270/ffmpeg-delete-chapters
          //  " -codec:s srt -map " + sCodecId
          Arguments = " -loglevel warning -i \""+ filePath + "\"" + " -sn -map_chapters -1 -codec:v" + 
            " copy -map 0:v:0 -acodec copy -map " + aCodecId + " \"" + mpegFilePath + "\"",
            // https://trac.ffmpeg.org/wiki/Encode/AAC
            //  40k bit rate 
            // " copy -map 0:v:0 -acodec libfdk_aac -profile:a aac_he_v2 -b:a 40k -map " + aCodecId + " \"" + mpegFilePath + "\"",
            
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
              mFileInfo.Update("Fail: media conversion");
              return;
            }

            Console.WriteLine("Elapsed time: " + Math.Round((ffmpegProcess.ExitTime - ffmpegProcess.
              StartTime).TotalMilliseconds) + " ms (change to mp4 container)");

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