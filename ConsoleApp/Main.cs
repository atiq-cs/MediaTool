// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using CommandLine;

  /// <summary>
  /// Entry Point class containing command line parser and instantiator main
  /// method
  /// Usage,
  ///  dotnet run -- --path D:\MyApp --simulate
  ///  dotnet run -- --rename --path D:\MyApp
  /// </summary>
  class MediaToolDemo {
    public class Options {
      [Option('r', "rename", Required = false, HelpText = "Rename files")]
      public bool shouldRename { get; set; }
      [Option('s',"simulate", Required = false, HelpText = "Simulate an action.")]
      public bool ShouldSimulate { get; set; }
      [Option("fv", Required = false, HelpText = "ffmpeg version")]
      public bool ShouldShowFFV { get; set; }
      [Option('p', "path", Required = false, HelpText = "Location of source file/s.")]
      public string Path { get; set; }
    }

    /// <summary>
    /// For now we are using to toogle boolean flags.
    /// Later ,check if we can actually do some sort of validation, the command line parser library
    /// should already provide it though
    /// </summary>
    static bool ValidateCommandLine(Options claOps) {
      string path = claOps.Path;
      if (string.IsNullOrEmpty(path) || (!System.IO.File.Exists(path) && !System.IO.Directory.
        Exists(path)))
        throw new ArgumentException("Invalid path specified!");
      return true;
    }

    static void Main(string[] args) {
      Parser.Default.ParseArguments<Options>(args)
        .WithParsed<Options>(o => {
          if (o.ShouldShowFFV)
            new MediaTool(o.ShouldShowFFV).ShowFFMpegVersion();
          else if (ValidateCommandLine(o)) {
            var app = new MediaTool(o.Path.EndsWith(@"\") ? o.Path.Substring(0, o.Path.Length -
              1) : o.Path, o.shouldRename, o.ShouldSimulate);
            app.Run();
            app.DisplaySummary();
          }
        });
      return;
    }
  }
}
