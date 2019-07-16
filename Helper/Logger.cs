﻿using System;
using System.Configuration;
using System.IO;

namespace migration_pair.Helper
{
    internal static class Logger
    {
        private static readonly string logFilePath = ConfigurationManager.AppSettings["Log_File_Path"];

        internal async static void Write(string message)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));

            await File.AppendAllTextAsync(logFilePath, string.Concat(DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ssK")));
        }
    }
}
