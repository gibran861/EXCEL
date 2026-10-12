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
                // 💡 UTILISER Windows-1252 / Latin1 comme FallbackEncoding :
                // - Les fichiers UTF-8 avec BOM seront détectés automatiquement par ExcelDataReader.
                // - Les fichiers ANSI / Windows-1252 (contenant des octets comme [E9] pour 'é') seront lus sans erreur.
                FallbackEncoding = System.Text.Encoding.GetEncoding(1252), 
                AutodetectSeparators = new[] { char.Parse(csvDelimiter), ',', '\t' }
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
                    UseHeaderRow = false // Conserver la ligne 0 brute pour le repérage
                }
            });

            if (result.Tables.Count > 0)
                return result.Tables[0]; // On retourne la première feuille du classeur
            
            throw new Exception("Le fichier est vide ou non structuré.");
        }
    }
}
    // Cette méthode lit un fichier Excel ou CSV et retourne son contenu sous forme de DataTable. mais elle genere erreur de UTF pour certaines fichiers 
    /*public DataTable LoadFileToDataTable(string filePath, string csvDelimiter = ";")
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
    }*/
}