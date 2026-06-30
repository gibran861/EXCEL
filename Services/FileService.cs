using System;
using System.Data;
using System.IO;
using ExcelDataReader;

public class FileService
{
    public DataTable LoadFileToDataTable(string filePath, string csvDelimiter = ";")
    {
        // Enregistrement requis pour le support des encodages de pages de code (ex: Windows-1252)
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        string extension = Path.GetExtension(filePath).ToLower();
        
        using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            IExcelDataReader reader;

            if (extension == ".csv")
            {
                reader = ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration
                {
                    FallbackEncoding = System.Text.Encoding.GetEncoding("utf-8"),
                    AutodetectSeparators = new[] { char.Parse(csvDelimiter) }
                });
            }
            else // Format .xls ou .xlsx
            {
                reader = ExcelReaderFactory.CreateReader(stream);
            }

            using (reader)
            {
                var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                {
                    ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                    {
                        UseHeaderRow = false // TRÈS IMPORTANT : Conserver la ligne 0 brute pour ne pas décaler le repérage visuel
                    }
                });

                if (result.Tables.Count > 0)
                    return result.Tables[0]; // On retourne la première feuille du classeur
                
                throw new Exception("Le fichier est vide ou non structuré.");
            }
        }
    }
}