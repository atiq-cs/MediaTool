## Media Tool
This is a Lite Video Converter Console Tool properly written with async support.

Background: mkv file can contain unnecessary subtitles and audio streams. There are i18n audio streams and subtitles that we are not interested in. This tool solves that problem.

At present this tool is an mkv to mp4 converter. It does folowing while performing conversion.
- extract eng subtitle as subrip
- if there are multiple audio channels only keep the eng audio to output mp4 along with the video
- corrects some metadata of the video


This media tool,
- Utilizes asynchronus tasks
- Utilizes ffmpeg to extract english subtitle properly and change container to mp4
- Can detect garbage subtitles files and can get rid of them
- Shorten advertisement durations for supported sources i.e., psa on Subtitles
- Shows media info.

### Example Usage
Here's some example runs,

    dotnet run -- update C:\ffmpeg simulate
    dotnet run -- update C:\ffmpeg
    dotnet run -- convert D:\Movies simulate
    dotnet run -- convert D:\Movies
    dotnet run -- --rename --path D:\Movies


#### Example Runs
Update ffmpeg,

    $ dotnet run -- update C:\PFiles_x64\PT\ffmpeg
    Local version: 5.1.1
    Latest stable found online: 5.1.2
    Waiting for ffmpeg dir lock to be freed..
    Downloading https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-5.1.2-full_build-shared.7z
    Temporarily saving to C:\PFiles_x64\PT\ffmpeg-5.1.2-full_build-shared.7z
    Removing file: C:\PFiles_x64\PT\ffmpeg-5.1.2-full_build-shared.7z

Or simulate,

    $ dotnet run -- update C:\PFiles_x64\PT\ffmpeg simulate
    Local version: 5.1.1
    Latest stable found online: 5.1.2
    Updating..


This does not work (don't add `--path`),

    $ dotnet run -- update --path C:\PFiles_x64\PT\ffmpeg simulate
    Unhandled Exception: System.InvalidOperationException: Provided ffmpeg binary path not found!
       at ConsoleApp.Updater..ctor(Boolean ShouldSimulate, String ffmpegLocation) in F:\Code\MediaTool\ConsoleApp\Updater.cs:line 30
       at ConsoleApp.MediaToolDemo.Main(String[] args) in F:\Code\MediaTool\ConsoleApp\Main.cs:line 84
       at ConsoleApp.MediaToolDemo.<Main>(String[] args)

First time install of ffmpeg requires following,

    New-Item -Type Directory C:\PFiles_x64\PT\ffmpeg
