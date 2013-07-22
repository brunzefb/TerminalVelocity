﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Illumina.TerminalVelocity
{
    public static class Downloader
    {
        public static Task DownloadAsync(this ILargeFileDownloadParameters parameters,
                                         CancellationToken? cancellationToken = null,
                                         IAsyncProgress<LargeFileDownloadProgressChangedEventArgs> progress = null,
                                         Action<string> logger = null)
        {
            CancellationToken ct = (cancellationToken != null) ? cancellationToken.Value : CancellationToken.None;
            Task task = Task.Factory.StartNew(
                () =>
                    {
                        //figure out number of chunks
                        int chunkCount = GetChunkCount(parameters.FileSize, parameters.MaxChunkSize);
                        int numberOfThreads = Math.Min(parameters.MaxThreads, chunkCount);
                        //create the file
                        Stream stream = parameters.GetOutputStream();
                        if (parameters.FileSize == 0) // Teminate Zero size files
                        {
                            if (progress != null)
                            {
                                progress.Report(new LargeFileDownloadProgressChangedEventArgs(100, 0, 0,
                                                                                              parameters.FileSize,
                                                                                              parameters.FileSize, "",
                                                                                              "",
                                                                                              null));
                            }
                            if (parameters.AutoCloseStream)
                                stream.Close();
                            return;
                        }
                        List<Task> downloadTasks = null;
                        try
                        {
                          
                            int currentChunk = 0;
                            var readStack = new ConcurrentStack<int>();
                           
                            //add all of the chunks to the stack
                            readStack.PushRange(Enumerable.Range(0, chunkCount).Reverse().ToArray());

                            // ReSharper disable AccessToModifiedClosure
                            Func<int, bool> shouldISlow = (int c) => (c - currentChunk) > (numberOfThreads*2);
                            // ReSharper restore AccessToModifiedClosure


                            var contentDic = new ConcurrentDictionary<int, byte[]>(numberOfThreads, 20);
                            var addEvent = new AutoResetEvent(false);

                             downloadTasks = new List<Task>(numberOfThreads);

                            for (int i = 0; i < numberOfThreads; i++)
                            {
                                downloadTasks.Add(CreateDownloadTask(parameters, contentDic, addEvent, readStack,
                                                                     shouldISlow, logger, ct));
                            }
                            //start all the download threads
                            downloadTasks.ForEach(x => x.Start());
                          
                            //start the write loop
                            while (currentChunk < chunkCount && !ct.IsCancellationRequested)
                            {
                                byte[] currentWrittenChunk;
                                if (contentDic.TryRemove(currentChunk, out currentWrittenChunk))
                                {
                                    //retry?
                                    logger(string.Format("writing: {0}", currentChunk));
                                    stream.Write(currentWrittenChunk, 0, currentWrittenChunk.Length);
                                    if (progress != null)
                                    {
                                        progress.Report(new LargeFileDownloadProgressChangedEventArgs((int)Math.Round(100 * (currentChunk/(float)chunkCount), 0), null));
                                    }
                                    currentChunk++;
                                }
                                else
                                {
                                    //are any of the treads alive?
                                    if (downloadTasks.Any(x => x != null && (x.Status == TaskStatus.Running || x.Status == TaskStatus.WaitingToRun)))
                                    {
                                        //wait for something that was added
                                        addEvent.WaitOne(100);
                                        addEvent.Reset();
                                    }
                                    else
                                    {
                                        throw new Exception("All threads were killed");
                                    }
                                }
                            }
                          
                        }
                            catch (Exception e)
             {
                // Report Failure
                progress.Report(
                    new LargeFileDownloadProgressChangedEventArgs(
                        100, 0, 0, parameters.FileSize, parameters.FileSize, "", "", null,true, e.Message));
                            }
                        finally
                        {
                            //kill all the tasks if exist
                            if (downloadTasks != null)
                            {
                                downloadTasks.ForEach(x =>
                                                          {
                                                              if (x == null) return;

                                                              ExecuteAndSquash(x.Dispose);
                                                          });
                            }
                            if (parameters.AutoCloseStream)
                            {
                                stream.Close();
                            }
                        }
                       
                    },
                ct);

            return task;
        }

        internal static Task CreateDownloadTask(ILargeFileDownloadParameters parameters,
                                                ConcurrentDictionary<int, byte[]> contentDic,
                                                AutoResetEvent reset, ConcurrentStack<int> readStack ,
                                                Func<int, bool> shouldSlowDown, Action<string> logger = null,
                                                CancellationToken? cancellation = null,
                                                Func<ILargeFileDownloadParameters, ISimpleHttpGetByRangeClient>
                                                    clientFactory = null)
        {
            cancellation = (cancellation != null) ? cancellation.Value : CancellationToken.None;
            var t = new Task(() =>
                                 {
                                     
                                     clientFactory = clientFactory ?? ((p) => new SimpleHttpGetByRangeClient(p.Uri));
                                     logger = logger ?? ((s) => { });

                                     ISimpleHttpGetByRangeClient client = clientFactory(parameters);
                                     int currentChunk = -1;
                                     readStack.TryPop(out currentChunk);
                                     int tries = 0;

                                     try
                                     {

                                         while (currentChunk >= 0 && tries < 4 &&
                                                !cancellation.Value.IsCancellationRequested) //-1 when we are done
                                         {
                                             logger(string.Format("downloading: {0}", currentChunk));
                                             SimpleHttpResponse response = null;
                                             try
                                             {
                                                 var dlTask = new Task(() => response = client.Get(parameters.Uri, GetChunkStart(currentChunk, parameters.MaxChunkSize),
                                                                       GetChunkSizeForCurrentChunk(parameters.FileSize, parameters.MaxChunkSize, currentChunk)));
                                                 dlTask.Start();
                                                 // if we're not done within 10 minutes then bail out since that download has something wrong
                                                 if (!dlTask.Wait(TimeSpan.FromMinutes(2)))
                                                 {
                                                     throw new Exception("Get operation cancelled because of a timeout");
                                                 }
                                             }
                                             catch (Exception e)
                                             {
                                                 if (e.InnerException != null)
                                                     logger(e.InnerException.Message);
                                                 else
                                                     logger(e.Message);

                                                 ExecuteAndSquash(client.Dispose);
                                                 client = clientFactory(parameters);
                                             }

                                             if (response != null && response.WasSuccessful)
                                             {
                                                 contentDic.AddOrUpdate(currentChunk, response.Content,
                                                                        (i, bytes) => response.Content);
                                                 tries = 0;
                                                 logger(string.Format("downloaded: {0}", currentChunk));
                                                 reset.Set();
                                                 if (!readStack.TryPop(out currentChunk))
                                                 {
                                                     currentChunk = -1;
                                                 }
                                                 while (shouldSlowDown(currentChunk))
                                                 {
                                                     logger(string.Format("throttling for chunk: {0}", currentChunk));
                                                     if (!cancellation.Value.IsCancellationRequested)
                                                     {
                                                         Thread.Sleep(500);
                                                     }
                                                 }
                                             }
                                             else if (response == null || response.IsStatusCodeRetryable)
                                             {
                                                 int sleepSecs = (int)Math.Pow(4.95, tries);
                                                 logger(string.Format("sleeping: {0}, {1}s", currentChunk, sleepSecs));
                                                 if (!cancellation.Value.IsCancellationRequested)
                                                 {
                                                     Thread.Sleep(sleepSecs * 1000); // 4s, 25s, 120s, 600s
                                                     tries++;
                                                 }
                                             }
                                             else
                                             {
                                                 throw new SimpleHttpClientException(response);
                                             }
                                         }
                                     }
                                     finally
                                     {
                                         if (currentChunk >= 0)
                                         {
                                             //put it back on the stack, if it's poison everyone else will die
                                             readStack.Push(currentChunk);
                                         }
                                         if (client != null)
                                         {
                                             ExecuteAndSquash(client.Dispose);
                                         }
                                     }

                                 }, cancellation.Value);
            return t;
        }

        internal static void ExecuteAndSquash( Action a)
        {
            try
            {
                a();
            }
            catch 
            {
            }
        }

        internal static long GetChunkStart(int currentChunk, int maxChunkSize)
        {
            return currentChunk*(long)maxChunkSize;
        }

        internal static int GetChunkCount(long fileSize, int chunkSize)
        {
            if (chunkSize == 0 || fileSize == 0) return 0;
            return (int) (fileSize/chunkSize + (fileSize%chunkSize > 0 ? 1 : 0));
        }

        internal static int GetChunkSizeForCurrentChunk(long fileSize, int maxChunkSize, int zeroBasedChunkNumber)
        {
            int chunkCount = GetChunkCount(fileSize, maxChunkSize);

            if (zeroBasedChunkNumber + 1 < chunkCount)
            {
                return maxChunkSize;
            }

            if (zeroBasedChunkNumber >= chunkCount)
            {
                return 0;
            }

            var remainder = (int) (fileSize%maxChunkSize);
            return remainder > 0 ? remainder : maxChunkSize;
        }
    }
}