﻿// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.Threading.Tasks;

  /// <summary>
  /// Entry Point class containing command line parser and instantiator main
  /// method
  /// Usage,
  ///  dotnet run -- update
  ///  dotnet run -- update simulate
  ///  dotnet run -- D:\MyApp simulate
  ///  dotnet run -- --rename --path D:\MyApp
  /// </summary>
  class MediaToolDemo {
    public class CommandLine {
      public string Path { get; private set; }
      public string Action { get; private set; }
      public bool ShouldSimulate { get; private set; }

      string[] arguments;
      public CommandLine(string[] arguments) {
        this.arguments = arguments;
      }

      /// <summary>
      /// First argument is action or Path.
      /// If first argument was an action then second arg must be path (for now)
      /// If first argumet is update then second argument as ffmpeg path is optional
      /// ToDo:
      ///  Supports following syntax, -- udpate simulate
      ///  postion of path in args will change for extract
      /// </summary>
      public bool ValidateCommandLine() {
        if (arguments == null || arguments.Length == 0)
          return false;
        int claIndex = 0;
        var actionSet = new System.Collections.Generic.HashSet<string>() { "convert", "extract",
          "merge", "update" };
        // validate arg 0
        if (arguments[0].Contains(':') || arguments[0].Contains('\\') || arguments[0].Length > 8)
          Path = arguments[claIndex++];
        else
          Action = arguments[claIndex++].ToLower();

        if (string.IsNullOrEmpty(Action)) { }
        else if (actionSet.Contains(Action)) {
          if (arguments.Length < 2) {
            if (Action != "update") {
              Console.WriteLine("File or dir Path should be specified!");
              return false;
            }
          }
          else
            Path = arguments[claIndex++];
        }
        else
          Path = Action;

        // verify path, path is mandatory if action is not update
        if ((Action != "update") && (string.IsNullOrEmpty(Path) || (!System.IO.File.Exists(Path) && !System.IO.Directory.
          Exists(Path)))) {
          Console.WriteLine("Invalid action or path specified!");
          return false;
        }

        // everything valid so far, assume default action
        if (string.IsNullOrEmpty(Action))
          Action = "convert";

        if (arguments[arguments.Length - 1] == "simulate")
          ShouldSimulate = true;
        return true;
      }
    }

    static async Task Main(string[] args) {
      var options = new CommandLine(args);
      if (options.ValidateCommandLine() == false)
        return ;
      if (options.Action == "update") {
        Updater app;
        if (string.IsNullOrEmpty(options.Path))
          app = new Updater(options.ShouldSimulate);
        else
          app = new Updater(options.ShouldSimulate, options.Path);
        await app.UpdateFFMpegAsync();
      }
      else {
        var app = new MediaTool(options.Path.EndsWith(@"\") ? options.Path.Substring(0, options.Path.Length -
          1) : options.Path, true, options.ShouldSimulate);
        app.Run();
        app.DisplaySummary();
      }
    }
  }
}
