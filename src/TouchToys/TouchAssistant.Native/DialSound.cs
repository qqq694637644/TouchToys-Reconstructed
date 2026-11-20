using System;
using System.IO;
using System.Media;
using System.Text;

namespace TouchAssistant.Native;

internal sealed class DialSound : IDisposable
{
	private SoundPlayer? player;

	private MemoryStream? wave;

	private long last;

	public void Tick()
	{
		long tickCount = Environment.TickCount64;
		if (tickCount - last < 45)
		{
			return;
		}
		last = tickCount;
		if (player == null)
		{
			wave = new MemoryStream();
			using (BinaryWriter binaryWriter = new BinaryWriter(wave, Encoding.ASCII, leaveOpen: true))
			{
				binaryWriter.Write(Encoding.ASCII.GetBytes("RIFF"));
				binaryWriter.Write(696);
				binaryWriter.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
				binaryWriter.Write(16);
				binaryWriter.Write((short)1);
				binaryWriter.Write((short)1);
				binaryWriter.Write(22050);
				binaryWriter.Write(44100);
				binaryWriter.Write((short)2);
				binaryWriter.Write((short)16);
				binaryWriter.Write(Encoding.ASCII.GetBytes("data"));
				binaryWriter.Write(660);
				Random random = new Random(17);
				for (int i = 0; i < 330; i++)
				{
					double num = (double)i / 22050.0;
					double num2 = (0.6 * Math.Sin(Math.PI * 3400.0 * num) + 0.4 * (random.NextDouble() * 2.0 - 1.0)) * Math.Exp((double)(-i) / 45.0) * 0.13;
					binaryWriter.Write((short)(num2 * 32767.0));
				}
			}
			wave.Position = 0L;
			player = new SoundPlayer(wave);
			player.Load();
		}
		player.Play();
	}

	public void Dispose()
	{
		player?.Stop();
		player?.Dispose();
		wave?.Dispose();
	}
}
