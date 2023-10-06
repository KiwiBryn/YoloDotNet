﻿using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace YoloDotNet.Data
{
    /// <summary>
    /// Abstract base class for performing object detection using a YOLOv8 model in ONNX format.
    /// </summary>
    public abstract class YoloBase : IDisposable
    {
        private readonly InferenceSession _session;
        public abstract IEnumerable<ResultModel> RunInference(Image img, double threshold);
        public OnnxModel OnnxModel { get; init; }

        /// <summary>
        /// Initializes a new instance of the Yolo base class.
        /// </summary>
        /// <param name="onnxModel">The path to the ONNX model file to use.</param>
        /// <param name="useCuda">Indicates whether to use CUDA for GPU acceleration.</param>
        /// <param name="gpuId">The GPU device ID to use when CUDA is enabled.</param>
        public YoloBase(string onnxModel, bool useCuda, int gpuId)
        {
            _session = useCuda
                ? new InferenceSession(onnxModel, SessionOptions.MakeSessionOptionWithCudaProvider(gpuId))
                : new InferenceSession(onnxModel);

            OnnxModel = _session.GetOnnxProperties();
        }

        /// <summary>
        /// Get tensors from an input image for object detection.
        /// </summary>
        /// <param name="img">The input image to extract tensors from.</param>
        /// <returns>A tensor containing pixel values extracted from the input image.</returns>
        public Tensor<float> GetTensors(Image img)
        {
            using var resizedImg = img.ResizeImage(OnnxModel.Input.Width, OnnxModel.Input.Height);

            var tensorPixels = resizedImg.ExtractPixelsFromImage(OnnxModel.Input.BatchSize, OnnxModel.Input.Channels);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(OnnxModel.InputName, tensorPixels)
            };

            using var output = _session.Run(inputs);

            // Get first sequence of maps
            var tensors = output.FirstOrDefault(x => x.Name == OnnxModel.OutputName)!.AsTensor<float>();

            return tensors ?? throw new Exception("No tensors found.");
        }

        /// <summary>
        /// Removes overlapping bounding boxes in a list of object detection results.
        /// </summary>
        /// <param name="predictions">The list of object detection results to process.</param>
        /// <returns>A filtered list with non-overlapping bounding boxes based on confidence scores.</returns>
        public static List<ResultModel> RemoveOverlappingBoxes(List<ResultModel> predictions)
        {
            var result = new List<ResultModel>();

            for (int i = 0; i < predictions.Count; i++)
            {
                bool keep = true;
                var item = predictions[i];
                var rect1 = item.Rectangle;

                for (int j = 0; j < result.Count; j++)
                {
                    var current = result[j];
                    var rect2 = current.Rectangle;

                    RectangleF intersection = RectangleF.Intersect(rect1, rect2);

                    float intArea = intersection.Width * intersection.Height; // intersection area
                    float unionArea = rect1.Width * rect1.Height + rect2.Width * rect2.Height - intArea; // union area
                    float overlap = intArea / unionArea; // overlap ratio

                    if (overlap >= 0.45f)
                    {
                        if (item.Confidence >= current.Confidence)
                            result[j] = item; // Replace the current overlapping box with the higher confidence one

                        keep = false;

                        break; // No need to check further, as this item overlaps with one already in the result list.
                    }
                }

                if (keep)
                {
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// Clamps a value within a specified range.
        /// </summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The minimum value in the range.</param>
        /// <param name="max">The maximum value in the range.</param>
        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        /// <summary>
        /// Releases resources and suppresses the finalizer for the current object.
        /// </summary>
        public void Dispose()
        {
            _session.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}