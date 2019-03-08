﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HN.Extensions;
using HN.Services;

namespace HN.Pipes
{
    /// <inheritdoc />
    /// <summary>
    /// 若当前的值是 <see cref="Uri" /> 类型，则该管道会进行处理。
    /// </summary>
    /// <typeparam name="TResult">加载目标的类型。</typeparam>
    public class UriPipe<TResult> : LoadingPipeBase<TResult> where TResult : class
    {
        private readonly IHttpClientFactory _httpClientFactory;

        /// <inheritdoc />
        /// <summary>
        /// 初始化 <see cref="UriPipe{TResult}" /> 类的新实例。
        /// </summary>
        /// <param name="designModeService">设计模式服务。</param>
        /// <param name="httpClientFactory">HttpClient 工厂。</param>
        public UriPipe(IDesignModeService designModeService, IHttpClientFactory httpClientFactory) : base(designModeService)
        {
            if (httpClientFactory == null)
            {
                throw new ArgumentNullException(nameof(httpClientFactory));
            }

            _httpClientFactory = httpClientFactory;
        }

        /// <inheritdoc />
        public override async Task InvokeAsync(ILoadingContext<TResult> context, LoadingPipeDelegate<TResult> next, CancellationToken cancellationToken = default(CancellationToken))
        {
            var uri = context.Current as Uri;
            if (uri == null)
            {
                await next(context, cancellationToken);
                return;
            }

            if (uri.IsHttp())
            {
                try
                {
                    var task = GetDownloadTask(uri, cancellationToken);
                    var (bytes, cacheControl) = await task;
                    context.Current = bytes;
                    await next(context, cancellationToken);
                    if (cacheControl != null && !cacheControl.NoCache)
                    {
                        context.HttpResponseBytes = bytes;
                    }
                }
                finally
                {
                    UriPipeInternal.DownloadTasks.Remove(uri);
                }
            }
            else if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                using (var fileStream = File.OpenRead(uri.AbsoluteUri.Substring("file:///".Length)))
                {
                    var buffer = new byte[fileStream.Length];
                    await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    context.Current = buffer;
                }
                await next(context, cancellationToken);
            }
            else if (string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
            {
                var base64 = uri.OriginalString;
                const string base64Head = "base64,";
                var base64Index = base64.IndexOf(base64Head, StringComparison.Ordinal);
                var bytes = Convert.FromBase64String(base64.Substring(base64Index + base64Head.Length));
                context.Current = bytes;
                await next(context, cancellationToken);
            }
            else
            {
                // pack://application:,,,/
                var streamResourceInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamResourceInfo != null)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await streamResourceInfo.Stream.CopyToAsync(memoryStream, 81920, cancellationToken);
                        context.Current = memoryStream.ToArray();
                    }
                    await next(context, cancellationToken);
                }
                else
                {
                    throw new FileNotFoundException();
                }
            }
        }

        private async Task<(byte[], CacheControlHeaderValue)> CreateDownloadTask(Uri uri, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("ImageEx");
            var response = await client.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var cacheControl = response.Headers.CacheControl;
            return (bytes, cacheControl);
        }

        private Task<(byte[], CacheControlHeaderValue)> GetDownloadTask(Uri uri, CancellationToken cancellationToken)
        {
            lock (UriPipeInternal.DownloadTasks)
            {
                Task<(byte[], CacheControlHeaderValue)> task;
                if (UriPipeInternal.DownloadTasks.TryGetValue(uri, out task))
                {
                    return task;
                }

                task = CreateDownloadTask(uri, cancellationToken);
                UriPipeInternal.DownloadTasks[uri] = task;
                return task;
            }
        }
    }

    internal class UriPipeInternal
    {
        internal static readonly Dictionary<Uri, Task<(byte[], CacheControlHeaderValue)>> DownloadTasks = new Dictionary<Uri, Task<(byte[], CacheControlHeaderValue)>>();
    }
}
