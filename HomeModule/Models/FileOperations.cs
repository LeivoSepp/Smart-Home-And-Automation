using HomeModule.Parameters;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HomeModule.Models
{
    class FileOperations
    {
        public string GetFilePath(string filename)
        {
            //the environment variable has been set through deployment.template.json file
            //basically it's a variable to use binding from host to container
            string mappedFolder = Environment.GetEnvironmentVariable(HomeParameters.CONTAINER_MAPPED_FOLDER);
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

            using (FileStream SourceStream = File.Open(filename, FileMode.OpenOrCreate))
            {
                await SourceStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }
    }
}
