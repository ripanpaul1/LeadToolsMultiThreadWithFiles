using Leadtools;
using Leadtools.Codecs;
using Leadtools.Forms.Auto;
using Leadtools.Ocr;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Services;
using System.Web.Services;

namespace LeadToolsService
{
    /// <summary>
    /// Summary description for OcrService
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    // [System.Web.Script.Services.ScriptService]
    public class OcrService : System.Web.Services.WebService
    {
        public struct TheVarSubDir
        {
            public static string License = "License";
            public static string OCRInput = "OCRInput";
            public static string OCRMasterFormSets = "OCRMasterFormSets";
        }
        private string[] GetFiles(string InputPath, string FileOrDir)
        {
            try
            {
                string[] _files = null;
                if (string.IsNullOrEmpty(FileOrDir))
                    _files = Directory.GetFiles(InputPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => (f.ToLower().EndsWith("pdf") || f.ToLower().EndsWith("tiff") || f.ToLower().EndsWith("tif")))
                        .ToArray();
                else
                {
                    if (Path.HasExtension(FileOrDir))
                        _files = Directory.GetFiles(InputPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => Path.GetFileName(f.ToLower()).Equals(FileOrDir.ToLower()))
                        .ToArray();
                    else
                        _files = Directory.GetFiles(Path.Combine(InputPath, FileOrDir), "*.*", SearchOption.AllDirectories)
                        .Where(f => (f.ToLower().EndsWith("pdf") || f.ToLower().EndsWith("tiff") || f.ToLower().EndsWith("tif")))
                        .ToArray();
                }
                if (_files == null)
                {
                    return null;
                }
                return _files;
            }
            catch
            {
                return null;
            }
        }

        [WebMethod]
        public List<string> ProcessFilesMultiThread(string FileOrDir)
        {
            //============================
            // root path
            string rootPath = Path.Combine("C:\\", "SMART");
            if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);
            if (!Directory.Exists(Path.Combine(rootPath, TheVarSubDir.License))) Directory.CreateDirectory(Path.Combine(rootPath, TheVarSubDir.License));
            if (!Directory.Exists(Path.Combine(rootPath, TheVarSubDir.OCRInput))) Directory.CreateDirectory(Path.Combine(rootPath, TheVarSubDir.OCRInput));
            if (!Directory.Exists(Path.Combine(rootPath, TheVarSubDir.OCRMasterFormSets))) Directory.CreateDirectory(Path.Combine(rootPath, TheVarSubDir.OCRMasterFormSets));
            //============================

            //============================
            // set the license
            RasterSupport.SetLicense(Path.Combine(rootPath, TheVarSubDir.License, "LEADTOOLS.lic"),
                File.ReadAllText(Path.Combine(rootPath, TheVarSubDir.License, "LEADTOOLS.lic.key")));

            // Ocr Engine started
            IOcrEngine TheOcrEngine = null;
            OcrEngineType engineType;
            if (!Enum.TryParse("LEAD", true, out engineType)) return null;
            if (engineType == OcrEngineType.LEAD)
            {
                TheOcrEngine = OcrEngineManager.CreateEngine(engineType, true);
                TheOcrEngine.Startup(null, null, null, null);

                TheOcrEngine.SettingManager.SetEnumValue("Recognition.Fonts.DetectFontStyles", 0);
                TheOcrEngine.SettingManager.SetBooleanValue("Recognition.Fonts.RecognizeFontAttributes", false);
                if (TheOcrEngine.SettingManager.IsSettingNameSupported("Recognition.RecognitionModuleTradeoff"))
                    TheOcrEngine.SettingManager.SetEnumValue("Recognition.RecognitionModuleTradeoff", "Accurate");
            }
            else
            {
                TheOcrEngine = OcrEngineManager.CreateEngine(engineType, true);
                TheOcrEngine.Startup(null, null, null, null);
            }

            // initialize RasterCodecs instance
            RasterCodecs _RasterCodecs = new RasterCodecs();

            // initialize DiskMasterFormsRepository instance
            DiskMasterFormsRepository _DiskMasterFormsRepository = new DiskMasterFormsRepository(_RasterCodecs, Path.Combine(rootPath, TheVarSubDir.OCRMasterFormSets));

            var managers = AutoFormsRecognitionManager.Ocr | AutoFormsRecognitionManager.Default;
            // initialize AutoFormsEngine instance
            AutoFormsEngine _AutoFormsEngine = new AutoFormsEngine(_DiskMasterFormsRepository, TheOcrEngine, null, managers, 30, 80, false)
            {
                UseThreadPool = TheOcrEngine != null && TheOcrEngine.EngineType == OcrEngineType.LEAD
            };
            //============================

            // files to be processed
            string[] _files = GetFiles(Path.Combine(rootPath, TheVarSubDir.OCRInput), FileOrDir);
            int fileCount = _files.Length;

            List<string> _FileResults = new List<string>();

            // Event to notify us when all work is finished 
            using (AutoResetEvent finishedEvent = new AutoResetEvent(false))
            {
                // Loop through all Files in the given Folder 
                foreach (string _file in _files)
                {
                    string _FileResult = null;

                    // Process it in a thread 
                    ThreadPool.QueueUserWorkItem((state) =>
                    {
                        try
                        {
                            // Process it 
                            //var _result = _AutoFormsEngine.Run(_file, null).RecognitionResult;    // geting error with this statement


                            var imageInfo = _RasterCodecs.GetInformation(_file, true);
                            var targetImage = _RasterCodecs.Load(_file, 0, CodecsLoadByteOrder.Bgr, 1, imageInfo.TotalPages);
                            targetImage.ChangeViewPerspective(RasterViewPerspective.TopLeft);
                            var _result = _AutoFormsEngine.Run(targetImage, null, targetImage, null).RecognitionResult;
                            if (_result == null) _FileResult = "Not Recognized";
                            else _FileResult = "Successfully Recognized";
                        }
                        catch (Exception ex)
                        {
                            _FileResult = "Not Recognized - " + ex.Message;
                        }
                        finally
                        {
                            _FileResults.Add(_FileResult);
                            if (Interlocked.Decrement(ref fileCount) == 0)
                            {
                                // We are done, inform the main thread 
                                finishedEvent.Set();
                            }
                        }
                    });
                }

                // Wait till all operations are finished 
                finishedEvent.WaitOne();
            }

            _AutoFormsEngine.Dispose();
            _RasterCodecs.Dispose();
            if (TheOcrEngine != null && TheOcrEngine.IsStarted)
                TheOcrEngine.Shutdown();

            return _FileResults;
        }

        
    }
}
