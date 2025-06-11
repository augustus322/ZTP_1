﻿using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ztp_projekt_1;

namespace MatrixMultiplicationBenchmark
{
	class Program
	{
		static void Main(string[] args)
		{
			// Benchmark
			var summary = BenchmarkRunner.Run<ImageFilterBenchmark>();


			//// Export images
			//double[,] blurFilter = new double[5, 5]
			//{
			//	{1/25.0, 1/25.0, 1/25.0, 1/25.0, 1/25.0},
			//	{1/25.0, 1/25.0, 1/25.0, 1/25.0, 1/25.0},
			//	{1/25.0, 1/25.0, 1/25.0, 1/25.0, 1/25.0},
			//	{1/25.0, 1/25.0, 1/25.0, 1/25.0, 1/25.0},
			//	{1/25.0, 1/25.0, 1/25.0, 1/25.0, 1/25.0}
			//};
			//Bitmap testImage = new Bitmap("C:\\Users\\PC COMPUTER\\repos\\Python\\obraz.jpg");

			//// Running ApplyFilterUnmanagedOptimized and ApplyFilterUnmanagedAffinity together may lead to artifacts on the image
			//Console.WriteLine("Filtrowanie...");
			//byte[,,] r1 = ApplyFilterManaged(testImage, blurFilter);
			//byte[,,] r2 = ApplyFilterUnmanaged(testImage, blurFilter);
			////byte[,,] r3 = ApplyFilterUnmanagedOptimized(testImage, blurFilter);
			////byte[,,] r4 = ApplyFilterUnmanagedAffinity(testImage, blurFilter);
			//byte[,,] r5 = ApplyFilterSIMD(testImage);
			//Console.WriteLine("Filtrowanie Zakończone.");

			//// Export
			//Console.WriteLine("Eksportowanie...");
			//Bitmap result1 = ExportToBitmap(r1);
			//Bitmap result2 = ExportToBitmap(r2);
			////Bitmap result3 = ExportToBitmap(r3);
			////Bitmap result4 = ExportToBitmap(r4);
			//Bitmap result5 = ExportToBitmap(r5);
			//result1.Save("r1.jpg", ImageFormat.Jpeg);
			//result2.Save("r2.jpg", ImageFormat.Jpeg);
			////result3.Save("r3.jpg", ImageFormat.Jpeg);
			////result4.Save("r4.jpg", ImageFormat.Jpeg);
			//result5.Save("r5.jpg", ImageFormat.Jpeg);
			//Console.WriteLine("Eksportowanie zakończone.");
		}
		static Bitmap ExportToBitmap(byte[,,] processedArray)
		{
			int height = processedArray.GetLength(0);
			int width = processedArray.GetLength(1);

			Bitmap result = new Bitmap(width, height);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Color pixel = Color.FromArgb(
						processedArray[y, x, 0],  // R
						processedArray[y, x, 1],  // G
						processedArray[y, x, 2]); // B

					result.SetPixel(x, y, pixel);
				}
			}
			return result;
		}

		// BREAKING POINT

		public static class NativeMethods
		{
			[DllImport("kernel32.dll")]
			public static extern IntPtr GetCurrentThread();

			[DllImport("kernel32.dll")]
			public static extern IntPtr GetCurrentProcess();

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);
		}

		static byte[,,] ApplyFilterManaged(Bitmap image, double[,] filter)
		{
			// Konwersja Bitmap -> byte[,,]
			byte[,,] colorArray = new byte[image.Height, image.Width, 3];
			for (int y = 0; y < image.Height; y++)
			{
				for (int x = 0; x < image.Width; x++)
				{
					Color pixel = image.GetPixel(x, y);
					colorArray[y, x, 0] = pixel.R;
					colorArray[y, x, 1] = pixel.G;
					colorArray[y, x, 2] = pixel.B;
				}
			}

			int width = colorArray.GetLength(0);
			int height = colorArray.GetLength(1);

			byte[,,] result = new byte[width, height, 3];

			for (int layer = 0; layer < 3; layer++)
			{
				for (int i = 2; i < width - 2; i++)
				{
					for (int j = 2; j < height - 2; j++)
					{
						double sum = 0;

						// Nakładanie filtra
						for (int ki = 0; ki < 5; ki++)
						{
							for (int kj = 0; kj < 5; kj++)
							{
								int ni = i + ki - 2;
								int nj = j + kj - 2;

								sum += colorArray[ni, nj, layer] * filter[ki, kj];
							}
						}
						result[i, j, layer] = (byte)Math.Clamp(sum, 0, 255);
					}
				}
			}
			return result;
		}

		static byte[,,] ApplyFilterUnmanaged(Bitmap image, double[,] filter)
		{
			BitmapData imageData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
			int bytesPerPixel = 3;

			int width = imageData.Width;
			int height = imageData.Height;
			int stride = imageData.Stride;

			byte[,,] result = new byte[height, width, 3];

			unsafe
			{
				byte* ptr = (byte*)imageData.Scan0;

				// First copy the original image to our result array
				for (int y = 0; y < height; y++)
				{
					byte* row = ptr + (y * stride);
					for (int x = 0; x < width; x++)
					{
						result[y, x, 2] = row[x * bytesPerPixel];     // B
						result[y, x, 1] = row[x * bytesPerPixel + 1]; // G
						result[y, x, 0] = row[x * bytesPerPixel + 2]; // R
					}
				}

				// Apply the filter
				for (int y = 2; y < height - 2; y++)
				{
					for (int x = 2; x < width - 2; x++)
					{
						for (int c = 0; c < 3; c++) // For each color channel
						{
							double sum = 0;

							// Apply 5x5 filter
							for (int fy = 0; fy < 5; fy++)
							{
								for (int fx = 0; fx < 5; fx++)
								{
									int nx = x + fx - 2;
									int ny = y + fy - 2;
									sum += result[ny, nx, c] * filter[fy, fx];
								}
							}
							// Update the result (we'll write back later)
							result[y, x, c] = (byte)Math.Clamp(sum, 0, 255);
						}
					}
				}
				// Write the result back to the bitmap
				for (int y = 2; y < height - 2; y++)
				{
					byte* row = ptr + (y * stride);
					for (int x = 2; x < width - 2; x++)
					{
						row[x * bytesPerPixel] = result[y, x, 2];     // B
						row[x * bytesPerPixel + 1] = result[y, x, 1]; // G
						row[x * bytesPerPixel + 2] = result[y, x, 0]; // R
					}
				}
			}

			image.UnlockBits(imageData);

			return result;
		}

		static byte[,,] ApplyFilterUnmanagedOptimized(Bitmap image, double[,] filter)
		{
			BitmapData imageData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
			int bytesPerPixel = 3;

			int width = imageData.Width;
			int height = imageData.Height;
			int stride = imageData.Stride;

			// 
			var pool = ArrayPool<byte>.Shared;
			byte[] rowBuffer = pool.Rent(stride);
			byte[,,] result = new byte[height, width, 3];

			try
			{
				unsafe
				{
					byte* ptr = (byte*)imageData.Scan0;

					// First copy the original image to our result array
					for (int y = 0; y < height; y++)
					{
						byte* row = ptr + (y * stride);

						Marshal.Copy((IntPtr)row, rowBuffer, 0, stride);

						for (int x = 0; x < width; x++)
						{
							result[y, x, 2] = row[x * bytesPerPixel];     // B
							result[y, x, 1] = row[x * bytesPerPixel + 1]; // G
							result[y, x, 0] = row[x * bytesPerPixel + 2]; // R
						}
					}

					fixed (byte* resultPtr = &result[0, 0, 0])
					{
						for (int c = 0; c < 3; c++)
						{
							for (int y = 2; y < height - 2; y++)
							{
								for (int x = 0; x < width; x++)
								{
									double sum = 0;

									// Unroll the filter loop for better performance
									sum += resultPtr[(y - 2) * width * 3 + (x - 2) * 3 + c] * filter[0, 0];
									sum += resultPtr[(y - 2) * width * 3 + (x - 1) * 3 + c] * filter[0, 1];
									sum += resultPtr[(y - 2) * width * 3 + x * 3 + c] * filter[0, 2];
									sum += resultPtr[(y - 2) * width * 3 + (x + 1) * 3 + c] * filter[0, 3];
									sum += resultPtr[(y - 2) * width * 3 + (x + 2) * 3 + c] * filter[0, 4];

									sum += resultPtr[(y - 1) * width * 3 + (x - 2) * 3 + c] * filter[1, 0];
									sum += resultPtr[(y - 1) * width * 3 + (x - 1) * 3 + c] * filter[1, 1];
									sum += resultPtr[(y - 1) * width * 3 + x * 3 + c] * filter[1, 2];
									sum += resultPtr[(y - 1) * width * 3 + (x + 1) * 3 + c] * filter[1, 3];
									sum += resultPtr[(y - 1) * width * 3 + (x + 2) * 3 + c] * filter[1, 4];

									sum += resultPtr[y * width * 3 + (x - 2) * 3 + c] * filter[2, 0];
									sum += resultPtr[y * width * 3 + (x - 1) * 3 + c] * filter[2, 1];
									sum += resultPtr[y * width * 3 + x * 3 + c] * filter[2, 2];
									sum += resultPtr[y * width * 3 + (x + 1) * 3 + c] * filter[2, 3];
									sum += resultPtr[y * width * 3 + (x + 2) * 3 + c] * filter[2, 4];

									sum += resultPtr[(y + 1) * width * 3 + (x - 2) * 3 + c] * filter[3, 0];
									sum += resultPtr[(y + 1) * width * 3 + (x - 1) * 3 + c] * filter[3, 1];
									sum += resultPtr[(y + 1) * width * 3 + x * 3 + c] * filter[3, 2];
									sum += resultPtr[(y + 1) * width * 3 + (x + 1) * 3 + c] * filter[3, 3];
									sum += resultPtr[(y + 1) * width * 3 + (x + 2) * 3 + c] * filter[3, 4];

									sum += resultPtr[(y + 2) * width * 3 + (x - 2) * 3 + c] * filter[4, 0];
									sum += resultPtr[(y + 2) * width * 3 + (x - 1) * 3 + c] * filter[4, 1];
									sum += resultPtr[(y + 2) * width * 3 + x * 3 + c] * filter[4, 2];
									sum += resultPtr[(y + 2) * width * 3 + (x + 1) * 3 + c] * filter[4, 3];
									sum += resultPtr[(y + 2) * width * 3 + (x + 2) * 3 + c] * filter[4, 4];

									resultPtr[y * width * 3 + x * 3 + c] = (byte)Math.Clamp(sum, 0, 255);
								}
							}
						}
					}
					// Write the result back to the bitmap
					for (int y = 2; y < height - 2; y++)
					{
						byte* row = ptr + (y * stride);
						for (int x = 2; x < width - 2; x++)
						{
							row[x * bytesPerPixel] = result[y, x, 2];     // B
							row[x * bytesPerPixel + 1] = result[y, x, 1]; // G
							row[x * bytesPerPixel + 2] = result[y, x, 0]; // R
						}
						Marshal.Copy(rowBuffer, 0, (IntPtr)row, stride);
					}
				}
			}
			finally
			{
				pool.Return(rowBuffer);
				image.UnlockBits(imageData);
			}

			return result;
		}

		static byte[,,] ApplyFilterUnmanagedAffinity(Bitmap image, double[,] filter)
		{
			// Set affinity (Example: restrict to core 0 and 1)
			IntPtr mask = new IntPtr(0x0003); // binary 11 -> CPU 0 and 1
			IntPtr currentThread = NativeMethods.GetCurrentThread();
			NativeMethods.SetThreadAffinityMask(currentThread, mask);

			IntPtr currentProcess = NativeMethods.GetCurrentProcess();
			NativeMethods.SetProcessAffinityMask(currentProcess, mask);

			BitmapData imageData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
			int bytesPerPixel = 3;

			int width = imageData.Width;
			int height = imageData.Height;
			int stride = imageData.Stride;

			var pool = ArrayPool<byte>.Shared;
			byte[] rowBuffer = pool.Rent(stride);
			byte[,,] result = new byte[height, width, 3];

			try
			{
				unsafe
				{
					byte* ptr = (byte*)imageData.Scan0;

					// First copy the original image to our result array
					for (int y = 0; y < height; y++)
					{
						byte* row = ptr + (y * stride);

						Marshal.Copy((IntPtr)row, rowBuffer, 0, stride);

						for (int x = 0; x < width; x++)
						{
							result[y, x, 2] = row[x * bytesPerPixel];     // B
							result[y, x, 1] = row[x * bytesPerPixel + 1]; // G
							result[y, x, 0] = row[x * bytesPerPixel + 2]; // R
						}
					}

					fixed (byte* resultPtr = &result[0, 0, 0])
					{
						for (int c = 0; c < 3; c++)
						{
							for (int y = 2; y < height - 2; y++)
							{
								for (int x = 0; x < width; x++)
								{
									double sum = 0;

									// Unroll the filter loop for better performance
									sum += resultPtr[(y - 2) * width * 3 + (x - 2) * 3 + c] * filter[0, 0];
									sum += resultPtr[(y - 2) * width * 3 + (x - 1) * 3 + c] * filter[0, 1];
									sum += resultPtr[(y - 2) * width * 3 + x * 3 + c] * filter[0, 2];
									sum += resultPtr[(y - 2) * width * 3 + (x + 1) * 3 + c] * filter[0, 3];
									sum += resultPtr[(y - 2) * width * 3 + (x + 2) * 3 + c] * filter[0, 4];

									sum += resultPtr[(y - 1) * width * 3 + (x - 2) * 3 + c] * filter[1, 0];
									sum += resultPtr[(y - 1) * width * 3 + (x - 1) * 3 + c] * filter[1, 1];
									sum += resultPtr[(y - 1) * width * 3 + x * 3 + c] * filter[1, 2];
									sum += resultPtr[(y - 1) * width * 3 + (x + 1) * 3 + c] * filter[1, 3];
									sum += resultPtr[(y - 1) * width * 3 + (x + 2) * 3 + c] * filter[1, 4];

									sum += resultPtr[y * width * 3 + (x - 2) * 3 + c] * filter[2, 0];
									sum += resultPtr[y * width * 3 + (x - 1) * 3 + c] * filter[2, 1];
									sum += resultPtr[y * width * 3 + x * 3 + c] * filter[2, 2];
									sum += resultPtr[y * width * 3 + (x + 1) * 3 + c] * filter[2, 3];
									sum += resultPtr[y * width * 3 + (x + 2) * 3 + c] * filter[2, 4];

									sum += resultPtr[(y + 1) * width * 3 + (x - 2) * 3 + c] * filter[3, 0];
									sum += resultPtr[(y + 1) * width * 3 + (x - 1) * 3 + c] * filter[3, 1];
									sum += resultPtr[(y + 1) * width * 3 + x * 3 + c] * filter[3, 2];
									sum += resultPtr[(y + 1) * width * 3 + (x + 1) * 3 + c] * filter[3, 3];
									sum += resultPtr[(y + 1) * width * 3 + (x + 2) * 3 + c] * filter[3, 4];

									sum += resultPtr[(y + 2) * width * 3 + (x - 2) * 3 + c] * filter[4, 0];
									sum += resultPtr[(y + 2) * width * 3 + (x - 1) * 3 + c] * filter[4, 1];
									sum += resultPtr[(y + 2) * width * 3 + x * 3 + c] * filter[4, 2];
									sum += resultPtr[(y + 2) * width * 3 + (x + 1) * 3 + c] * filter[4, 3];
									sum += resultPtr[(y + 2) * width * 3 + (x + 2) * 3 + c] * filter[4, 4];

									resultPtr[y * width * 3 + x * 3 + c] = (byte)Math.Clamp(sum, 0, 255);
								}
							}
						}
					}
					// Write the result back to the bitmap
					for (int y = 2; y < height - 2; y++)
					{
						byte* row = ptr + (y * stride);
						for (int x = 2; x < width - 2; x++)
						{
							row[x * bytesPerPixel] = result[y, x, 2];     // B
							row[x * bytesPerPixel + 1] = result[y, x, 1]; // G
							row[x * bytesPerPixel + 2] = result[y, x, 0]; // R
						}
						Marshal.Copy(rowBuffer, 0, (IntPtr)row, stride);
					}
				}
			}
			finally
			{
				pool.Return(rowBuffer);
				image.UnlockBits(imageData);
			}

			return result;
		}

		static byte[,,] ApplyFilterSIMD(Bitmap image)
		{
			int width = image.Width;
			int height = image.Height;
			int bytesPerPixel = 3;
			int kernelSize = 5;
			int kernelOffset = kernelSize / 2;
			float kernelFactor = 1f / 25f; // for a uniform 5x5 blur

			BitmapData imageData = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
			int stride = imageData.Stride;

			float[] red = new float[width * height];
			float[] green = new float[width * height];
			float[] blue = new float[width * height];

			float[] resultRed = new float[width * height];
			float[] resultGreen = new float[width * height];
			float[] resultBlue = new float[width * height];

			byte[,,] result = new byte[height, width, 3];

			unsafe
			{
				byte* src = (byte*)imageData.Scan0;

				// Extract channels into planar arrays
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						int pixelIndex = y * stride + x * bytesPerPixel;
						int index = y * width + x;
						blue[index] = src[pixelIndex];
						green[index] = src[pixelIndex + 1];
						red[index] = src[pixelIndex + 2];
					}
				}
			}

			image.UnlockBits(imageData);

			void BlurChannelSIMD(float[] src, float[] dst)
			{
				int vectorSize = Vector<float>.Count;

				for (int y = kernelOffset; y < height - kernelOffset; y++)
				{
					int x = kernelOffset;
					for (; x <= width - kernelOffset - vectorSize; x += vectorSize)
					{
						Vector<float> acc = Vector<float>.Zero;

						for (int ky = -kernelOffset; ky <= kernelOffset; ky++)
						{
							for (int kx = -kernelOffset; kx <= kernelOffset; kx++)
							{
								int offset = (y + ky) * width + (x + kx);
								acc += new Vector<float>(src, offset);
							}
						}

						acc *= kernelFactor;
						acc.CopyTo(dst, y * width + x);
					}

					// Scalar remainder
					for (; x < width - kernelOffset; x++)
					{
						float sum = 0f;
						for (int ky = -kernelOffset; ky <= kernelOffset; ky++)
						{
							for (int kx = -kernelOffset; kx <= kernelOffset; kx++)
							{
								int offset = (y + ky) * width + (x + kx);
								sum += src[offset];
							}
						}
						dst[y * width + x] = sum * kernelFactor;
					}
				}
			}

			// Apply blur
			BlurChannelSIMD(red, resultRed);
			BlurChannelSIMD(green, resultGreen);
			BlurChannelSIMD(blue, resultBlue);

			// Combine result channels into byte[,,]
			for (int y = kernelOffset; y < height - kernelOffset; y++)
			{
				for (int x = kernelOffset; x < width - kernelOffset; x++)
				{
					int index = y * width + x;
					result[y, x, 0] = (byte)Math.Clamp(resultRed[index], 0, 255);
					result[y, x, 1] = (byte)Math.Clamp(resultGreen[index], 0, 255);
					result[y, x, 2] = (byte)Math.Clamp(resultBlue[index], 0, 255);
				}
			}

			return result;
		}
	}
}