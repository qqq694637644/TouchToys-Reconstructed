using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace TouchAssistant.Services;

internal static class MotionReference
{
	internal static async Task ExtractAsync(string input, string output)
	{
		Directory.CreateDirectory(output);
		MediaClip clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(input));
		MediaComposition composition = new MediaComposition
		{
			Clips = { clip }
		};
		File.WriteAllText(Path.Combine(output, "duration.txt"), clip.OriginalDuration.ToString());
		for (int i = 0; i < 24; i++)
		{
			using ImageStream image = await composition.GetThumbnailAsync(TimeSpan.FromTicks(clip.OriginalDuration.Ticks * i / 24), 640, 0, VideoFramePrecision.NearestFrame);
			using Stream stream = image.AsStreamForRead();
			using FileStream dest = File.Create(Path.Combine(output, $"frame-{i:00}.jpg"));
			await stream.CopyToAsync(dest);
		}
	}
}
