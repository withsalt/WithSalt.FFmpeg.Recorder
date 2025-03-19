using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Interface;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    internal class FileInputArgumentsBuilder : BaseInputArgumentsBuilder, IFileInputArgumentsBuilder
    {
        private List<string> _inputFiles = new List<string>();

        private List<IArgument> _inputArgumentList = new List<IArgument>();
        public FileInputArgumentsBuilder()
        {
            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));
        }

        public IFileInputArgumentsBuilder WithFiles(IEnumerable<string> files)
        {
            if (files == null || files.Count() == 0)
            {
                throw new ArgumentNullException(nameof(files));
            }
            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    throw new ArgumentException($"File {file} not found");
                }
                _inputFiles.Add(file);
            }
            return this;
        }

        public IFileInputArgumentsBuilder WithFiles(params string[] files)
        {
            return WithFiles(files.ToList());
        }

        public override FFMpegArgumentProcessor Build()
        {
            if (this._inputFiles.Count == 0)
            {
                throw new ArgumentException("Please first construct the input file list via the WithFiles API.");
            }

            SizeArgument? size = this._outputArgumentList.FirstOrDefault(s => s.GetType().FullName == typeof(SizeArgument).FullName) as SizeArgument;
            if (size?.Size == null)
            {
                throw new ArgumentException("The output resolution must be specified. via the WithOutputSize API.");
            }

            if (_inputFiles.Count == 1)
            {
                _arguments = FFMpegArguments.FromFileInput(_inputFiles[0], false, opt =>
                {
                    foreach (var argument in _inputArgumentList)
                    {
                        opt.WithArgument(argument);
                    }
                });
            }
            else
            {
                //多个文件时，需要用过滤器统一不同视频参数
                var filterComplexArg = CreateFilterComplexArgument(_inputFiles, size.Size.Value.Width, size.Size.Value.Height);
                _filterArgumentList.Add(filterComplexArg);
                _filterArgumentList.Add(new CustomArgument("-map \"[v]\""));

                _arguments = FFMpegArguments.FromFileInput(_inputFiles, false, opt =>
                {
                    foreach (var argument in _inputArgumentList)
                    {
                        opt.WithArgument(argument);
                    }
                });
            }

            return base.Build();
        }


        private IArgument CreateFilterComplexArgument(List<string> inputFiles, int width, int height)
        {
            var filterParts = new List<string>();

            // 为每个输入生成缩放和格式化参数
            for (int i = 0; i < inputFiles.Count; i++)
            {
                filterParts.Add($"[{i}:v]format=yuv420p,scale={width}:{height},setsar=1[v{i}]");
            }

            // 生成concat拼接部分
            if (inputFiles.Count > 0)
            {
                var concatInputs = string.Join("", Enumerable.Range(0, inputFiles.Count).Select(n => $"[v{n}]"));
                filterParts.Add($"{concatInputs}concat=n={inputFiles.Count}:v=1:a=0[v]");
            }

            string filters = string.Join(";", filterParts);
            return new CustomArgument($"-filter_complex \"{filters}\"");
        }
    }
}
