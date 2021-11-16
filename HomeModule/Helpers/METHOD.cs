using System;
using System.IO;
using System.Threading.Tasks;

namespace HomeModule.Helpers
{
    class METHOD
    {
        public static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }
        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            //dtDateTime = dtDateTime.AddHours(DateTimeTZ().Offset.TotalHours);
            return dtDateTime;
        }
        public static DateTimeOffset DateTimeTZ()
        {
            TimeZoneInfo eet = TimeZoneInfo.FindSystemTimeZoneById("EET");
            TimeSpan timeSpan = eet.GetUtcOffset(DateTime.UtcNow);
            DateTimeOffset LocalTimeTZ = new DateTimeOffset(DateTime.UtcNow).ToOffset(timeSpan);
            return LocalTimeTZ;
        }
        public static bool TimeBetween(DateTimeOffset datetime, TimeSpan start, TimeSpan end)
        {
            TimeSpan now = datetime.TimeOfDay;
            // see if start comes before end
            if (start < end)
                return start <= now && now <= end;
            // start is after end, so do the inverse comparison
            return !(end < now && now < start);
        }
        public string GetFilePath(string filename)
        {
            //the environment variable has been set through deployment.template.json file
            //basically it's a variable to use binding from host to container
            string mappedFolder = Environment.GetEnvironmentVariable(CONSTANT.CONTAINER_MAPPED_FOLDER);
            if (!Directory.Exists(mappedFolder))
            {
                Directory.CreateDirectory(mappedFolder);
            }
            filename = mappedFolder + "/" + filename;
            return filename;
        }
        public async Task<string> OpenExistingFile(string filename)
        {
            byte[] buffer;
            using (FileStream sr = File.OpenRead(filename))
            {
                buffer = new byte[(int)sr.Length];
                await sr.ReadAsync(buffer, 0, (int)sr.Length);
            }
            string output = System.Text.Encoding.UTF8.GetString(buffer);
            return output;
        }
        public async Task SaveStringToLocalFile(string filename, string content)
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(content.ToCharArray());
            if (File.Exists(filename)) File.Delete(filename);

            using FileStream SourceStream = File.Open(filename, FileMode.OpenOrCreate);
            await SourceStream.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}
