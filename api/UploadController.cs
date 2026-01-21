using System.Globalization;
using api;
using Azure.Storage.Files.Shares;
using CsvHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Data;
using Azure;
using CsvHelper.Configuration;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            using var csvStream = new MemoryStream();
            await file.CopyToAsync(csvStream);
            csvStream.Position = 0;
            using var conn = new SqliteConnection("Data Source=:memory:;Cache=Shared");
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE Test (
                        aap TEXT,
                        example TEXT,
                        random INTEGER,
                        stoffer TEXT);";
                        cmd.ExecuteNonQuery();
            }

            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, new CsvConfiguration (CultureInfo.InvariantCulture)
            {
                Delimiter = ";",
                HasHeaderRecord = true
            });

            var records = csv.GetRecords<Test>().ToList();

            foreach(var r in records)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Test (aap, example, random, stoffer)
                    VALUES (@aap, @example, @random, @stoffer);";
                
                cmd.Parameters.AddWithValue("@aap", r.aap);
                cmd.Parameters.AddWithValue("@example", r.example);
                cmd.Parameters.AddWithValue("@random", r.random);
                cmd.Parameters.AddWithValue("@stoffer", r.stoffer);

                cmd.ExecuteNonQuery();
            }

            var tempFile = Path.GetTempFileName();
            var sqliteStream = new MemoryStream();

            using (var exportConn = new SqliteConnection("Data Source=file:exportdb?mode=memory&cache=shared"))
            {
                exportConn.Open();

                conn.BackupDatabase(exportConn);

                using (var cmd = exportConn.CreateCommand())
                {
                    cmd.CommandText = "VACUUM INTO @dest";
                    cmd.Parameters.AddWithValue("@dest", tempFile);
                    cmd.ExecuteNonQuery();
                }
            }

            byte[] sqliteData = System.IO.File.ReadAllBytes(tempFile); //Ska vara File. även nere.
            sqliteStream.Write(sqliteData, 0, sqliteData.Length);
            sqliteStream.Position = 0;
            System.IO.File.Delete(tempFile);

            string connectionString = "DefaultEndpointsProtocol=https;AccountName=csvtodb;AccountKey=???;EndpointSuffix=core.windows.net";
            string shareName = "testingdb"; 
            string mydirName = "testdir"; 

            ShareClient myshare = new ShareClient(connectionString, shareName);
            myshare.Create();
            ShareDirectoryClient directory = myshare.GetDirectoryClient(mydirName);
            directory.Create();
            ShareFileClient myfile = directory.GetFileClient("test.db");
            await myfile.CreateAsync(sqliteStream.Length);
            await myfile.UploadRangeAsync(new HttpRange(0, sqliteStream.Length), sqliteStream);
            return Ok();
        }
    }
}
