# Media Tool
A media tool to automate some actions on media files.
- Utilizes asynchronus tasks
- Utilizes ffmpeg to extract english subtitle properly and change container to mp4
- Can detect garbage subtitles files and can get rid of them
- Shorten advertisement durations for supported sources i.e., psa

## Example Usage
Here's some example runs,
```Powershell
dotnet run -- update D:\ffmpeg simulate
dotnet run -- update D:\ffmpeg
dotnet run -- convert D:\Movies simulate
dotnet run -- convert D:\Movies
dotnet run -- --rename --path D:\Movies
```

### Example Runs
Update ffmpeg,

    $ dotnet run -- update D:\PFiles_x64\PT\ffmpeg
    Local version: 4.1.4
    Latest stable found online: 4.2.1
    Waiting for ffmpeg dir lock to be freed..
    Updating..
    Downloading https://ffmpeg.zeranoe.com/builds/win64/shared/ffmpeg-4.2.1-win64-shared.zip
    Temporarily saving to D:\PFiles_x64\PT\ffmpeg-4.2.1-win64-shared.zip

Or simulate,

    $ dotnet run -- update D:\PFiles_x64\PT\ffmpeg simulate
    Local version: 4.2.1
    Latest stable found online: 4.2.1


This does not work (don't add `--path`),

    $ dotnet run -- update --path D:\PFiles_x64\PT\ffmpeg simulate
    Unhandled Exception: System.InvalidOperationException: Provided ffmpeg binary path not found!
       at ConsoleApp.Updater..ctor(Boolean ShouldSimulate, String ffmpegLocation) in D:\git_ws\MediaTool\ConsoleApp\Updater.cs:line 30
       at ConsoleApp.MediaToolDemo.Main(String[] args) in D:\git_ws\MediaTool\ConsoleApp\Main.cs:line 84
       at ConsoleApp.MediaToolDemo.<Main>(String[] args)
