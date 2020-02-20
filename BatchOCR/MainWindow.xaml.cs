using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Data.SQLite;

namespace BatchOCR
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DirectoryInfo fileFolder;

        private string dbFolder = "";

        private string dbPath = "";

        private List<Process> subProcesses = new List<Process>();

        private bool hasCancelled = false;

        private CancellationTokenSource cancelToken = new CancellationTokenSource();

        public MainWindow()
        {
            InitializeComponent();
        }

        // Handler til at finde sti til filer
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = folderBrowserDialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    fileFolder = new DirectoryInfo(folderBrowserDialog.SelectedPath);
                    PathTextBox.Text = fileFolder.FullName;
                }
            }
        }

        // Handler til at finde mappe hvor databasen skal ligge
        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = folderBrowserDialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    dbFolder = folderBrowserDialog.SelectedPath;
                    DbPathTextBox.Text = dbFolder;
                }
            }
        }

        // Handler til at vælge kvalitet
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ((System.Windows.Controls.RadioButton)sender).IsChecked = true;
        }

        // Handler til paralelliseret gennemløb
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {

            if (fileFolder != null)
            {
                try
                {
                    //// Test at Tesseract findes
                    Process prc = new Process();
                    ProcessStartInfo info = new ProcessStartInfo();
                    info.FileName = "tesseract.exe";
                    info.WindowStyle = ProcessWindowStyle.Hidden;
                    prc.StartInfo = info;
                    prc.Start();
                    prc.WaitForExit();

                    ProcessListBox.Items.Clear();
                    ProcessProgress.Value = 0;
                    RunButton.IsEnabled = false;
                    CancelButton.IsEnabled = true;
                    FindFilePathButton.IsEnabled = false;
                    FindDbPathButton.IsEnabled = false;
                    RadioButtonStackPanel.IsEnabled = false;
                    PathTextBox.IsEnabled = false;
                    DbPathTextBox.IsEnabled = false;

                    // Kør OCR-skanningen asynkront
                    bool isHQ = HQRadioButton.IsChecked.Value;
                    Task.Factory.StartNew(() => RunOCR(isHQ));
                }
                catch (System.ComponentModel.Win32Exception exep)
                {
                    Dispatcher.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show("Tesseract kunne ikke startes. Tjek at Tesseract er installeret og PATH-variabel er sat.\n" + exep.Message);
                    });
                }

            }
            else
            {
                System.Windows.MessageBox.Show("Du skal vælge en mappe!");
            }

        }

        // Handler til annuller-knap
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            hasCancelled = true;
            ProcessListBox.Items.Add("Brugeren har annulleret processen. Igangværende filer afsluttes. Vent venligst ... ");
            ((System.Windows.Controls.Button)sender).IsEnabled = false;

            // dræb alle skabte Tesseract-processer, der ikke er afsluttet
            foreach (Process p in subProcesses)
            {
                if (!p.HasExited)
                {
                    p.Kill();
                }
            }

            cancelToken.Cancel();
        }

        // Metode der gennemløber filer parallelliseret
        private void RunOCR(bool isHQ)
        {
            try
            {
                // Start tidtagning
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                // Find fulde stier til alle tif-file (inkl. i undermapper)
                FileInfo[] files = fileFolder.GetFiles("*.tif", SearchOption.AllDirectories);

                // Sæt maksimum på statusbar
                Dispatcher.Invoke(() =>
                {
                    ProcessProgress.Maximum = files.Length;
                    ProgressTextBlock.Text = "0 filer ud af " + files.Length + " behandlet";
                });

                // Databasesti
                if (dbFolder == "")
                {
                    dbFolder = fileFolder.FullName;
                    dbPath = fileFolder + @"\" + "OCRdatabase.db";

                    Dispatcher.Invoke(() =>
                    {
                        DbPathTextBox.Text = dbFolder;
                    });
                }
                else
                {
                    dbPath = dbFolder + @"\" + "OCRdatabase.db";
                }

                // Opret databasefil
                SQLiteConnection.CreateFile(dbPath);

                // Forbind til database
                using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;"))
                {
                    conn.Open();

                    // Opret tabel
                    string sql = "CREATE TABLE records (relativePath TEXT NOT NULL, pageNumber INTEGER NOT NULL, ocrText TEXT NOT NULL)";
                    using (SQLiteCommand command = new SQLiteCommand(sql, conn))
                    {
                        command.ExecuteNonQuery();
                    }

                    conn.Close();
                }

                // Opret midlertidig mappe til OCR-testfiler
                DirectoryInfo tempFolderPath = new DirectoryInfo(Environment.CurrentDirectory + @"\temp");
                tempFolderPath.Create();
              
                // Løb igennem vha. parallelliseret loop med maksimalt parallellisering svarende til antal processorer
                var options = new ParallelOptions();
                options.CancellationToken = cancelToken.Token;
                options.MaxDegreeOfParallelism = Environment.ProcessorCount;

                AddMessage("Behandler " + ((files.Length == 1) ? "én" : files.Length.ToString()) + " filer på " + options.MaxDegreeOfParallelism + " processorer ...");

                Parallel.For(0, files.Length, options, i =>
                {
                    try
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();
                        RunOCR(files[i], tempFolderPath + @"\" + i, isHQ);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                });

                // Opgør tiden
                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);

                AddMessage("OCR-behandlingen er færdig. Processeringstid: " + elapsedTime);

            }
            catch (IOException e)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show("Der skete en I/O-fejl: \n" + e.Message);
                });
            }
            catch (SQLiteException e)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show("Der skete en database-fejl: \n" + e.Message);
                });
            }
            catch (OperationCanceledException)
            {
                AddMessage("Processeringen blev stoppet!");
            }
            catch (Exception e)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show("Der skete en fejl: \n" + e.Message);
                });
            }
            finally
            {
                // Slet midlertidig mappe
                DirectoryInfo tempFolderPath = new DirectoryInfo(Environment.CurrentDirectory + @"\temp");
                tempFolderPath.Delete();

                // Aktiver kontrolelementer
                Dispatcher.Invoke(() =>
                {
                    RunButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                    FindFilePathButton.IsEnabled = true;
                    FindDbPathButton.IsEnabled = true;
                    RadioButtonStackPanel.IsEnabled = true;
                    PathTextBox.IsEnabled = true;
                    DbPathTextBox.IsEnabled = true;
                });

            }
        }

        // Metoder der OCR'er én fil
        private void RunOCR(FileInfo currentFile, string targetFileFullNameMinusExtension, bool isHq)
        {
            try
            {
                // Generér unik side-separator
                string pageSeparator = "[Separator " + Guid.NewGuid().ToString() + "]";

                // 
                string doInvert = "-c tessedit_do_invert=0 ";

                // Vælg tessdata-fil afhængig af valg af kvalitet
                string dataFilePath = @".\tessdata-fast";
                if (isHq)
                {
                    dataFilePath = @".\tessdata-best";
                    doInvert = "";
                }

                // Kør tesseract i baggrunden         
                string strCmdText = "\"" + currentFile.FullName + "\" \"" + targetFileFullNameMinusExtension + "\" -l dan -c page_separator=\"" + pageSeparator + "\" " + doInvert + "--tessdata-dir \"" + dataFilePath + "\"";
                Console.WriteLine(strCmdText);
                Process prc = new Process();
                subProcesses.Add(prc);
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "tesseract.exe";
                info.Arguments = strCmdText;
                prc.StartInfo = info;
                prc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                prc.Start();
                prc.WaitForExit();

                // Læs teksten som streng og opdel i sider
                string currentText = File.ReadAllText(targetFileFullNameMinusExtension + ".txt");
                string[] currentPages = currentText.Split(new string[] { pageSeparator }, StringSplitOptions.RemoveEmptyEntries);

                // Find den relative sti for filen
                string currentFileRelPath = currentFile.FullName.Replace(fileFolder.FullName + @"\", "");

                // Skriv til database
                using (SQLiteConnection dbConnection = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;"))
                {

                    dbConnection.Open();

                    using (SQLiteCommand command = new SQLiteCommand(dbConnection))
                    {
                        using (var transaction = dbConnection.BeginTransaction())
                        {
                            command.CommandText = "INSERT INTO records (relativePath, pageNumber, ocrText) VALUES (@relativePath, @pageNumber, @ocrText)";

                            for (int i = 0; i < currentPages.Length; i++)
                            {
                                command.Parameters.AddWithValue("@relativePath", currentFileRelPath);
                                command.Parameters.AddWithValue("@pageNumber", i + 1);
                                command.Parameters.AddWithValue("@ocrText", currentPages[i]);

                                command.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                    }

                    dbConnection.Close();
                }

                // Udskriv resultat
                if (!hasCancelled)
                {
                    AddMessage("Filen " + currentFileRelPath + " (" + ((currentPages.Length == 1) ? "1 side" : currentPages.Length + " sider") + ") blev OCR-behandlet og gemt i databasen.");
                }
                else
                {
                    AddMessage("Processeringen af filen " + currentFileRelPath + " blev afbrudt.");
                }
            }            
            catch (Exception e)
            {
                AddMessage("Det skete en fejl: " + e.Message);
            }
            finally
            {
                // Tesseract-outputfil slettes
                FileInfo file = new FileInfo(targetFileFullNameMinusExtension + ".txt");
                file.Delete();

                // Opdater procesbaren
                Dispatcher.Invoke(() =>
                {
                    ProcessProgress.Value++;
                    ProgressTextBlock.Text = ProcessProgress.Value + " filer ud af " + ProcessProgress.Maximum + " behandlet";
                });
            }

        }

        // Hjælpemetode til at opdatere teksten i GUI'en fra en anden tråd
        public void AddMessage(string text)
        {
            Dispatcher.Invoke(() =>
            {
                ProcessListBox.Items.Add(text);
            });
        }
    }
}
