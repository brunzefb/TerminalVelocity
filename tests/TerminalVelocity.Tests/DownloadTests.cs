﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using Moq;
using NUnit.Framework;
using ThreadState = System.Threading.ThreadState;

namespace Illumina.TerminalVelocity.Tests
{
    
    [TestFixture]
    public class DownloadTests
    {
        public DownloadTests()
        {
            Debug.Listeners.Add(new DefaultTraceListener());
        }
       
        [Test]
        public void SimpleGetClientGetsFirst100Bytes()
        {
            var timer = new Stopwatch();
            timer.Start();
            var uri = new Uri(Constants.ONE_GIG_FILE);
            var client = new SimpleHttpGetByRangeClient(uri);
            var response =client.Get(uri, 0, 100);
            timer.Stop();
            Debug.WriteLine("total {0}ms or {1}secs", timer.ElapsedMilliseconds, timer.ElapsedMilliseconds/1000);
            Assert.NotNull(response);
            Assert.True(response.ContentLength == 100);
            Assert.True(response.ContentRangeLength == 1297662912);
            Assert.True(response.ContentRangeStart == 0);
            Assert.True(response.ContentRangeStop == 99);
            Assert.True(response.StatusCode == 206);
            Assert.NotNull(response.Content);
            Assert.True(response.ContentLength == response.Content.Length);
        }

        [TearDown]
        public void TearDown()
        {
            File.Delete(SafePath("sites_vcf.gz"));
        }
		
        [Test]
        public void ReadStackReturnsSequentially()
        {
            var readStack = new ConcurrentStack<int>();
            //add all of the chunks to the stack
            readStack.PushRange(Enumerable.Range(0, 5).Reverse().ToArray());
            for (int i = 0; i < 5; i++)
            {
                int current;
                readStack.TryPop(out current);
                Assert.True(current == i);
            }
           
        }

        [Test]
        public void ChunkCalculationsForLargeFiles()
        {
            int maxChunkSize = 5242880;
            int currentChunk = 411;
            long fileSize = 141705080397;
            long chunkStart = Downloader.GetChunkStart(currentChunk, maxChunkSize);
            Assert.True(chunkStart == 2154823680);
            long chunkSize = Downloader.GetChunkSizeForCurrentChunk(fileSize, maxChunkSize, currentChunk);
            Assert.True(chunkSize == maxChunkSize);
        }

        
        [TestCase(16 * 1024, 10), TestCase(0,10), TestCase(2, 10), TestCase(1024, 10), TestCase(1024*1024, 80), TestCase(5 * (1024*1024), 400)]
        public void ExpectedDownloadTimeCalculation(int chunkSize, int expected)
        {
            Assert.AreEqual( expected, Downloader.ExpectedDownloadTimeInSeconds(chunkSize));
        }

        //Sujit: no longer valid because now the progress is calculated using bytes written and filesize
        //[Test]
        //public void TestProgressBarComputation()
        //{
        //    int chunkCount = 10000;
        //    for (int zeroBasedChunkNumber = 0; zeroBasedChunkNumber <= chunkCount-1; zeroBasedChunkNumber++)
        //    {
        //        var progressIndicatorValue = Downloader.ComputeProgressIndicator(zeroBasedChunkNumber, chunkCount);
        //        Console.WriteLine("{0}:{1}", zeroBasedChunkNumber, progressIndicatorValue);

        //        Assert.IsTrue(
        //            (zeroBasedChunkNumber == 0 && progressIndicatorValue == 1)
        //            || ((zeroBasedChunkNumber + 1) != chunkCount && progressIndicatorValue != 100) 
        //            || ((zeroBasedChunkNumber + 1) == chunkCount && progressIndicatorValue == 100)
        //            );
        //    }
        //}

        [Test]
        public void IfFileSizeIsZeroReturn100PercentProgress()
        {            
            int actual = Downloader.ComputeProgressIndicator(0, 0);
            Assert.AreEqual(100, actual);
        }

        //Sujit: no longer valid because now the progress is calculated using bytes written and filesize
        //[Test]
        //public void ProgressBarShowsCorrectWhenChunkCountIsOne()
        //{
        //    int chunkCount = 1;
        //    int actual = Downloader.ComputeProgressIndicator(0, 1);
        //  Assert.AreEqual(100, actual);
        //}

        //[Test]
        //public void ThrottleDownloadWhenQueueIsFull()
        //{
        //    var parameters = new LargeFileDownloadParameters(new Uri(Constants.ONE_GIG_FILE_S_SL), "blah", 1000);
        //    var writeQueue = new ConcurrentQueue<ChunkedFilePart>();
        //    var e = new AutoResetEvent(false);
            
        //    byte[] sampleResponse = Encoding.UTF8.GetBytes("hello world");
        //    var mockClient = new Mock<ISimpleHttpGetByRangeClient>();

        //    mockClient.Setup(x => x.Get(It.IsAny<Uri>(), It.IsAny<long>(), It.IsAny<long>()))
        //              .Returns(new SimpleHttpResponse(206, sampleResponse, null));
        //    int timesAskedForSlow = -1;
            
        //    var readStack = new ConcurrentStack<int>();
        //    //add all of the chunks to the stack
        //    readStack.PushRange(Enumerable.Range(0, 5).Reverse().ToArray());
        //    Func<int, bool> shouldSlw = i =>
        //                                    {
        //                                        timesAskedForSlow++;
        //                                        return true;
        //                                    };
        //    var bufferManager = new BufferManager(new[] { new BufferQueueSetting(SimpleHttpGetByRangeClient.BUFFER_SIZE, 1), new BufferQueueSetting((uint)parameters.MaxChunkSize) });
        //    var task = new Downloader(bufferManager, parameters,writeQueue ,e, readStack, shouldSlw,Downloader.ExpectedDownloadTimeInSeconds(parameters.MaxChunkSize), clientFactory: (x) => mockClient.Object );
        //    task.Start();
        //    task.Wait(2000);
        //    try
        //    {
        //        task.Dispose();
        //    }catch{}
        //    Assert.True(timesAskedForSlow > 1, "time asked for slow" + timesAskedForSlow );
        //    int next;
        //    readStack.TryPop(out next);
        //    Assert.True(next == 2);
        //    ChunkedFilePart part;
        //    writeQueue.TryDequeue(out part);
        //    Assert.True(Encoding.UTF8.GetString(part.Content) == "hello world");

        //}

        //[Test]
        //public void PutBackOnStackWhenFailed()
        //{
        //    var parameters = new LargeFileDownloadParameters(new Uri(Constants.ONE_GIG_FILE_S_SL), "blah", 1000);
        //    var writeQueue = new ConcurrentQueue<ChunkedFilePart>();
        //    var e = new AutoResetEvent(false);

        //    byte[] sampleResponse = Encoding.UTF8.GetBytes("hello world");
        //    var mockClient = new Mock<ISimpleHttpGetByRangeClient>();

        //    mockClient.Setup(x => x.Get(It.IsAny<Uri>(), It.IsAny<long>(), It.IsAny<long>()))
        //              .Throws(new Exception("hahaha"));
        //    int timesAskedForSlow = -1;

        //    var readStack = new ConcurrentStack<int>();
        //    //add all of the chunks to the stack
        //    readStack.PushRange(Enumerable.Range(0,2).Reverse().ToArray());
        //    Func<int, bool> shouldSlw = i =>
        //    {
        //        timesAskedForSlow++;
        //        return true;
        //    };
        //    try
        //    {
        //        var bufferManager = new BufferManager(new []{new BufferQueueSetting(SimpleHttpGetByRangeClient.BUFFER_SIZE, 1), new BufferQueueSetting((uint)parameters.MaxChunkSize )  });
        //        var ct = new CancellationTokenSource();
        //        var task = new Downloader(bufferManager, parameters, writeQueue, e, readStack, shouldSlw, Downloader.ExpectedDownloadTimeInSeconds(parameters.MaxChunkSize),
        //                                                 clientFactory: (x) => mockClient.Object, cancellation: ct.Token);
        //        task.Start();
        //        task.Wait(5000);
        //        ct.Cancel();
        //        task.Wait(10000);
        //        task.Dispose();

        //    }
        //    catch
        //    {
        //    }
        //    Assert.True(readStack.Count == 2);
        //    int next;
        //    readStack.TryPop(out next);
        //    Assert.True(next == 0);
        //}

        [Test]
        public void CancellationTokenWillCancel()
        {
            var parameters = new LargeFileDownloadParameters(new Uri(Constants.ONE_GIG_FILE_S_SL), "blah", 1000, verifyLength: false);
            var writeQueue = new ConcurrentQueue<ChunkedFilePart>();

            byte[] sampleResponse = Encoding.UTF8.GetBytes("hello world");
            var mockClient = new Mock<ISimpleHttpGetByRangeClient>();

            mockClient.Setup(x => x.Get(It.IsAny<Uri>(), It.IsAny<long>(), It.IsAny<long>()))
                      .Returns(() =>
            {
                Thread.Sleep(200);
                return new SimpleHttpResponse(206, sampleResponse, null);
            });

            int timesAskedForSlow = -1;
            var readStack = new ConcurrentStack<int>();
            //add all of the chunks to the stack
            readStack.PushRange(Enumerable.Range(0, 5).Reverse().ToArray());
            Func<int, bool> shouldSlw = i =>
            {
                timesAskedForSlow++;
                return false;
            };
            var tokenSource = new CancellationTokenSource();
            var failureToken = new FailureToken();
            var bufferManager = new BufferManager(new[] { new BufferQueueSetting(SimpleHttpGetByRangeClient.BUFFER_SIZE, 1), new BufferQueueSetting((uint)parameters.MaxChunkSize) });
            var task = new Downloader(bufferManager, parameters, writeQueue, readStack, shouldSlw, Downloader.ExpectedDownloadTimeInSeconds(parameters.MaxChunkSize), clientFactory: (x) => mockClient.Object, failureToken: failureToken, cancellation: tokenSource.Token);
            task.Start();
            Thread.Sleep(500);
            tokenSource.Cancel();
            task.Wait(TimeSpan.FromMinutes(2));
            int current;
            readStack.TryPop(out current);
            Assert.True(current != 10); //we shouldn't get to 10 before the cancel works

        }

        [Test]
        public void CalculateChunkCalcs()
        {
            long fileSize = 29996532;
            int maxChunk = LargeFileDownloadParameters.DEFAULT_MAX_CHUNK_SIZE;
            var chunkCount =Downloader.GetChunkCount(fileSize, maxChunk);
            Assert.True(chunkCount == 6);
            long totalBytes = 0;
            long lastChunkStart = 0;
            int lastChunkLength = maxChunk;
            for (int i = 0; i < chunkCount; i++)
            {
                var chunkStart = Downloader.GetChunkStart(i, maxChunk);
                var chunkLength = Downloader.GetChunkSizeForCurrentChunk(fileSize, maxChunk, i);
                Debug.WriteLine(string.Format("chunk {0} start {1} length {2}", i, chunkStart, chunkLength));
                totalBytes += chunkLength;
                lastChunkStart = chunkStart;
                lastChunkLength = chunkLength;
            }
            Assert.True(totalBytes == fileSize);
            Assert.True(lastChunkStart + lastChunkLength == fileSize);

        }

          [TestCase(1), TestCase(2),  TestCase(4),  TestCase(8)]
        public void ParallelChunkedDownload(int threadCount)
        {
          
            var uri = new Uri(Constants.TWENTY_MEG_FILE);
            var path = SafePath("sites_vcf.gz");
            Action<string> logger = ( message) => Trace.WriteLine(message);
            var timer = new Stopwatch();
              timer.Start();
            ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path, 29996532, null, maxThreads: threadCount);
            Task task = parameters.DownloadAsync(logger: logger);
            task.Wait(TimeSpan.FromMinutes(5));
            timer.Stop();
            Debug.WriteLine("Took {0} threads {1} ms", threadCount, timer.ElapsedMilliseconds);
            //try to open the file
            ValidateGZip(path, parameters.FileSize, Constants.TWENTY_CHECKSUM);
        }



          //[TestCase(4)]
          //public void SimulateThreadHangAndRecovery(int threadCount)
          //{

          //    var uri = new Uri(Constants.TWENTY_MEG_FILE);
          //    var path = SafePath("sites_vcf.gz");
          //    Action<string> logger = (message) => Trace.WriteLine(message);
          //    var timer = new Stopwatch();
          //    timer.Start();
          //    ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path, Constants.TWENTY_MEG_FILE_LENGTH, null, threadCount);
          //    Task task = parameters.DownloadAsync(logger: logger);

          //    List<Downloader> dlrList = Downloader.ParentThreadToDownloaders.Values.FirstOrDefault();

          //    while (dlrList == null || dlrList.Count < threadCount)
          //    {
          //        dlrList = Downloader.ParentThreadToDownloaders.Values.FirstOrDefault();
          //        Thread.Sleep(200);
          //    }
          //    var rnd = new Random((int)DateTime.Now.Ticks);
          //    var threadToKill = rnd.Next(dlrList.Count());

          //    dlrList[threadToKill].SimulateTimedOut = true;
          //    var sw = new Stopwatch();
          //    sw.Start();
          //    var gotKilled = false;

          //    // wait for the thread to be killed within a few seconds
          //    while (sw.ElapsedMilliseconds < 10000)
          //    {
          //        if (dlrList[threadToKill].DownloadWorkerThread.ThreadState == ThreadState.Aborted)
          //        {
          //            gotKilled = true;
          //            break;
          //        }
          //    }

          //    Assert.True(gotKilled);

          //    // make sure the thread gets respawned within a few seconds
          //    sw.Restart();
          //    var gotReplaced = false;
          //    while (sw.ElapsedMilliseconds < 10000)
          //    {
          //        var thread = dlrList[threadToKill].DownloadWorkerThread;

          //        if (thread == null)
          //            continue;

          //        if (thread.ThreadState == ThreadState.Running || thread.ThreadState == ThreadState.Stopped)
          //        {
          //            gotReplaced = true;
          //            break;
          //        }
          //    }

          //    Assert.IsTrue(gotReplaced);

          //    // make sure we have a successful download
          //    task.Wait(TimeSpan.FromMinutes(2));
          //    timer.Stop();
          //    Debug.WriteLine("Took {0} threads {1} ms", threadCount, timer.ElapsedMilliseconds);
          //    //try to open the file
          //    ValidateGZip(path, parameters.FileSize, Constants.TWENTY_CHECKSUM);

          //}

          [TestCase(2, 2), TestCase(2, 5)]
          public void MultipleParallelChunkedDownload(int threadCount, int parallelFactor)
          {
              Action<string> download = (prefix) => {
                                          var uri = new Uri(Constants.TWENTY_MEG_FILE);
                                          var path = SafePath(prefix +"sites_vcf.gz");
                                          Action<string> logger = (message) => Trace.WriteLine(message);
                                          var timer = new Stopwatch();
                                          timer.Start();
                                          ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(
                                              uri, path, 29996532,null, maxThreads: threadCount);
                                          Task task = parameters.DownloadAsync(logger: logger);
                                          task.Wait(TimeSpan.FromMinutes(5));
                                          timer.Stop();
                                          Debug.WriteLine("Took {0} threads {1} ms", threadCount,
                                                          timer.ElapsedMilliseconds);
                                          //try to open the file
                                          ValidateGZip(path, parameters.FileSize, Constants.TWENTY_CHECKSUM);
               
              };
              var tasks = new List<Task>();
              for (int i = 0; i < parallelFactor; i++)
              {
                  int i1 = i;
                  Task t = Task.Factory.StartNew(() => download(i1.ToString()));
                  tasks.Add(t);
              }
              Task.WaitAll(tasks.ToArray());

          }

          [TestCase(32, Category = "time-consuming")]
          public void ParallelChunkedOneGig(int threadCount)
          {
              string path = "";
              try
              {
                  var uri = new Uri(Constants.ONE_GIG_FILE_S_SL);
                  path = SafePath("sites_vcf.gz");
                  Action<string> logger = (message) => { };
                  var timer = new Stopwatch();
                  //a list to hold measured transfer rates
                  var transferRateList = new List<double>();
                  var asyncProgress = new AsyncProgress<LargeFileDownloadProgressChangedEventArgs>(
                      obj =>
                      {
                          transferRateList.Add(obj.DownloadBitRate);
                          logger("progress");
                      });

                  var manager = new BufferManager(new[] { new BufferQueueSetting(SimpleHttpGetByRangeClient.BUFFER_SIZE, (uint)threadCount), new BufferQueueSetting(LargeFileDownloadParameters.DEFAULT_MAX_CHUNK_SIZE) });
                  ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path, 1297662912, null, maxThreads: threadCount);
                  
                  timer.Start();
                  
                  Task task = parameters.DownloadAsync(logger: logger, bufferManager: manager, progress: asyncProgress);
                  
                  task.Wait(TimeSpan.FromMinutes(25));
                  timer.Stop();

                  var averageTransferRate = transferRateList.Average();
                  Debug.WriteLine("Took {0} threads {1} ms with average transfer rate of {2}", threadCount, timer.ElapsedMilliseconds, averageTransferRate);
                  //try to open the file
                  ValidateGZip(path, parameters.FileSize, Constants.ONE_GIG_CHECKSUM);

                  //We expect that the approximate bytes transferred (calculated using average transfer rate) is with maximim error of 30% (accuracy of atleast 70%)
                  Assert.AreEqual(parameters.FileSize, averageTransferRate * timer.Elapsed.TotalSeconds, 0.30 * parameters.FileSize);
              }
              finally
              {
                  File.Delete(path);
              }
          }

          //[TestCase(Category = "time-consuming")]
          //public void ParallelChunkedThirteenGig(int threadCount = 16)
          //{
          //    string path = "";
          //    try
          //    {
          //        var uri = new Uri(Constants.THIRTEEN_GIG_BAD_SAMPLE);
          //        path = SafePath("RZ-UHR_S1_L001_R2_001.fastq.gz");
          //        Action<string> logger = (message) => { };
          //        var timer = new Stopwatch();
          //        timer.Start();
          //        var manager = new BufferManager(new[] { new BufferQueueSetting(SimpleHttpGetByRangeClient.BUFFER_SIZE, (uint)threadCount), new BufferQueueSetting(LargeFileDownloadParameters.DEFAULT_MAX_CHUNK_SIZE) });
          //        ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path, Constants.THIRTEEN_GIG_FILE_LENGTH, maxThreads: threadCount);
          //        Task task = parameters.DownloadAsync(logger: logger, bufferManager: manager);
          //        task.Wait(TimeSpan.FromMinutes(30));
          //        timer.Stop();
          //        Debug.WriteLine("Took {0} threads {1} ms", threadCount, timer.ElapsedMilliseconds);
          //        //try to open the file
          //        ValidateGZip(path, parameters.FileSize, Constants.THIRTEEN_GIG_CHECKSUM);

          //    }
          //    finally
          //    {
          //        File.Delete(path);
          //    }
          //}


          [Test()]
          public void DownloadZeroByteFile()
          {
              var uri = new Uri(Constants.ZERO_BYTE_FILE);
              string path = SafePath("sites_vcf.gz");
              Action<string> logger = (message) => { };
              var timer = new Stopwatch();
              timer.Start();
              ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path,null, null);
              Task task = parameters.DownloadAsync(logger: logger);
              task.Wait(TimeSpan.FromMinutes(15));
              timer.Stop();
              Debug.WriteLine("Took {0} threads {1} ms", parameters.MaxThreads, timer.ElapsedMilliseconds);
              //try to open the file
              ValidateGZip(path, parameters.FileSize, Constants.ZERO_BYTE_CHECKSUM);
          }

        internal static void ValidateGZip(string path, long fileSize, string checksum)
        {
            using (Stream fs = File.OpenRead(path))
            {
                Assert.True(fs.Length == fileSize);
                string actualChecksum = Md5SumByProcess(path);
                Assert.AreEqual(checksum, actualChecksum);
                Debug.WriteLine("file checksum succesfully validated");
            }

        }

        [Test]
        public void ValidateSpeedOfWebRequest()
        {
            var uri = new Uri(Constants.TWENTY_MEG_FILE);
            var path = SafePath("sites_vcf.gz");
            Action<string> logger = (message) => Trace.WriteLine(message);
            var timer = new Stopwatch();
            timer.Start();
            var client = new WebClient();
            client.DownloadFile(uri, path);
            timer.Stop();
            Debug.WriteLine("Took {0} threads {1} ms", 1, timer.ElapsedMilliseconds);
            ValidateGZip(path, 29996532, Constants.TWENTY_CHECKSUM);
            
        }

        //[Test]
        //public void CheckMd5()
        //{
         
        //    string result = Md5SumByProcess(@"C:\Users\groberts\Downloads\UnitTestFile_110120");
        //    Assert.True(result != null);
        //}

        public static string Md5SumByProcess(string file)
        {
            var p = new Process();
            string md5Path = Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent.Parent.Parent.Parent.FullName,"lib","fciv.exe");
            p.StartInfo.FileName = md5Path;
            p.StartInfo.Arguments = string.Format(@"-add ""{0}""",file);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.Start();
            p.WaitForExit();
            string output = p.StandardOutput.ReadToEnd().Replace(@"//
// File Checksum Integrity Verifier version 2.05.
//
", "");
            return output.Split(' ')[0];
        }

       

        [Test]
        public void SimpleGetClientCanDownloadTwentyMegFileSynchronously()
        {
            var timer = new Stopwatch();
            var uri = new Uri(Constants.TWENTY_MEG_FILE);
            var client = new SimpleHttpGetByRangeClient(uri);
            var path = SafePath("sites_vcf.gz");
            timer.Start();

            using (FileStream output = File.Create(path))
            {
                const int chunksize = 1024*1000*2;
                var response = client.Get(uri, 0, chunksize);
                output.Write(response.Content, 0, (int)response.ContentLength);
              
                long currentFileSize = (int)response.ContentLength;
                long fileSize = response.ContentRangeLength.Value;
                while (currentFileSize < fileSize)
                {
                    SimpleHttpResponse loopResponse;
                    long left = fileSize - currentFileSize;
                    Debug.WriteLine("chunk start {0} length {1} ", currentFileSize, left < chunksize ? left : chunksize);
                    loopResponse = client.Get(new Uri(Constants.TWENTY_MEG_FILE), currentFileSize, left < chunksize ? left : chunksize);
                    output.Write(loopResponse.Content, 0, (int)loopResponse.ContentLength);
                    currentFileSize += loopResponse.ContentLength;
                }

            }
            
            timer.Stop();
            Debug.WriteLine("total {0}ms or {1}secs", timer.ElapsedMilliseconds, timer.ElapsedMilliseconds / 1000);
            
            using (Stream fs = File.OpenRead(path))
            {
                using (Stream gzipStream =  new GZipInputStream(fs) )
                {
                    using (var reader = new StreamReader(gzipStream))
                    {
                        reader.Read();
                    }
                }
            }
        }


        [Test]
        public void DownloadSmallerFile()
        {
            //8 threads
            //MaxChunkSize = 1048576
            //FileSize = 5242880
           
            var uri = new Uri(Constants.FIVE_MEG_FILE);
            var path = SafePath("sites_vcf.gz");
            Action<string> logger = (message) => { };

            var transferRateList = new List<double>();
            var asyncProgress = new AsyncProgress<LargeFileDownloadProgressChangedEventArgs>(
                obj =>
                {
                    transferRateList.Add(obj.DownloadBitRate);
                    Debug.WriteLine(obj.DownloadBitRate);
                    logger("progress");
                });

            var timer = new Stopwatch();
            timer.Start();
            ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path, 1048576, null, maxThreads: 8);
            Task task = parameters.DownloadAsync(logger: logger, progress:asyncProgress);
            task.Wait(TimeSpan.FromMinutes(1));
            timer.Stop();
            var averageTransferRate = transferRateList.Average();

            //try to open the file
            ValidateGZip(path, parameters.FileSize, Constants.FIVE_MEG_CHECKSUM);

            if (averageTransferRate > 0)// sometimes tx rate is 0, possibly due to caching
            {
                Debug.WriteLine("Took {0} threads {1} ms with average transfer rate of {2}", 8, timer.ElapsedMilliseconds, averageTransferRate);
                //We expect that the approximate bytes transferred (calculated using average transfer rate) is with maximim error of 40% (accuracy of atleast 60%) because it is a small file and txrate is calculated once every two seconds.
                Assert.GreaterOrEqual(averageTransferRate * timer.Elapsed.TotalSeconds, 0.40 * parameters.FileSize);
            }
        }

        [Test]
        public void DownloadSmallerInSingleChunk()
        {
           
            var uri = new Uri(Constants.FIVE_MEG_FILE);
            var path = SafePath("sites_vcf.gz");
            Action<string> logger = (message) => { };
            var timer = new Stopwatch();
            timer.Start();
            
            ILargeFileDownloadParameters parameters = new LargeFileDownloadParameters(uri, path, 1048576, maxChunkSize: Constants.FIVE_MEG_LENGTH, maxThreads: 1);
            Task task = parameters.DownloadAsync(logger: logger, progress: new AsyncProgress<LargeFileDownloadProgressChangedEventArgs>( s=> logger("progress")) );
            task.Wait(TimeSpan.FromMinutes(5));
            timer.Stop();
            Debug.WriteLine("Took {0} threads {1} ms", 1, timer.ElapsedMilliseconds);
            //try to open the file
            ValidateGZip(path, parameters.FileSize, Constants.FIVE_MEG_CHECKSUM);
        }

 

        [Test]
        public void TestStaleWriteEmptyStack()
        {
            var laterDate = DateTime.Now.AddMinutes(-10);
            var concStack = new ConcurrentStack<int>();//empty stack
            Assert.True(Downloader.NeedToCheckForUnwrittenChunks(concStack, laterDate, 5));
        }

        [Test]
        public void TestStaleWriteNonEmptyStack()
        {
            var laterDate = DateTime.Now.AddMinutes(-10);
            var concStack = new ConcurrentStack<int>();//empty stack
            concStack.Push(10);
            Assert.False(Downloader.NeedToCheckForUnwrittenChunks(concStack, laterDate, 5));
        }

        public static string SafePath(string fileName)
        {
            string path = Path.Combine(Environment.CurrentDirectory,fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return path;
        }
    }
}
