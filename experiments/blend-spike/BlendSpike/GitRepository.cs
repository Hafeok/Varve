// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BlendSpike;

/// <summary>Reads files at a commit through git's plumbing, never a checkout.</summary>
internal sealed class GitRepository(string root)
{
    /// <summary>The first-parent commits of HEAD that touch the paths, oldest first.</summary>
    public IReadOnlyList<string> FirstParentCommitsTouching(IEnumerable<string> pathspecs)
    {
        List<string> arguments = ["log", "--first-parent", "--reverse", "--format=%H", "HEAD", "--"];
        arguments.AddRange(pathspecs);
        return Lines(Run(arguments));
    }

    /// <summary>The commit's subject line and author date.</summary>
    public string Describe(string commit) =>
        Run(["show", "-s", "--format=%h %ad %s", "--date=short", commit]).Trim();

    /// <summary>Every path in the commit's tree.</summary>
    public IReadOnlyList<string> ListFiles(string commit) => Lines(Run(["ls-tree", "-r", "--name-only", commit]));

    /// <summary>The content of each path at the commit, as UTF-8 text.</summary>
    public Dictionary<string, string> ReadFiles(string commit, IReadOnlyList<string> paths)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        if (paths.Count == 0)
        {
            return files;
        }

        ProcessStartInfo start = Start(["cat-file", "--batch"]);
        start.RedirectStandardInput = true;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        StringBuilder request = new();
        foreach (string path in paths)
        {
            request.Append(commit).Append(':').Append(path).Append('\n');
        }

        // Write on another thread: git answers while it reads, and a large
        // request would otherwise fill both pipes.
        System.Threading.Tasks.Task writer = System.Threading.Tasks.Task.Run(() =>
        {
            process.StandardInput.Write(request.ToString());
            process.StandardInput.Close();
        });

        Stream output = process.StandardOutput.BaseStream;
        foreach (string path in paths)
        {
            string header = ReadLine(output);
            string[] parts = header.Split(' ');
            if (parts.Length < 3 || parts[1] == "missing")
            {
                continue;
            }

            int size = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            byte[] content = new byte[size];
            output.ReadExactly(content);
            _ = output.ReadByte();
            files[path] = Encoding.UTF8.GetString(content);
        }

        writer.Wait();
        process.WaitForExit();
        return files;
    }

    private static string ReadLine(Stream stream)
    {
        List<byte> line = [];
        int b;
        while ((b = stream.ReadByte()) is not -1 and not '\n')
        {
            line.Add((byte)b);
        }

        return Encoding.UTF8.GetString([.. line]);
    }

    private string Run(IReadOnlyList<string> arguments)
    {
        using Process process = Process.Start(Start(arguments)) ?? throw new InvalidOperationException("git did not start");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " exited " + process.ExitCode);
        }

        return output;
    }

    private ProcessStartInfo Start(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static List<string> Lines(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
