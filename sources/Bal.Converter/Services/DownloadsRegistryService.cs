﻿using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Bal.Converter.Common.Enums;
using Bal.Converter.Modules.Downloads;
using Bal.Converter.YouTubeDl.Quality;

using LiteDB;

namespace Bal.Converter.Services ;

public class DownloadsRegistryService : IDownloadsRegistryService, IDisposable
{
    private readonly ILiteDatabase database;
    private readonly SemaphoreSlim downloadSemaphore;
    private readonly SemaphoreSlim fetchSemaphore;

    private readonly ILiteCollection<DownloadJob> collection;
    private readonly ObservableCollection<DownloadJob> jobs;

    public DownloadsRegistryService(ILiteDatabase database)
    {
        this.database = database;
        this.collection = this.database.GetCollection<DownloadJob>();

        this.jobs = new ObservableCollection<DownloadJob>(this.collection.Query().ToList());
        this.jobs.CollectionChanged += this.OnCollectionChanged;

        this.fetchSemaphore = new SemaphoreSlim(this.collection.Query().Where(x => x.State == DownloadState.Pending || x.State == DownloadState.Fetching).Count());
        this.downloadSemaphore = new SemaphoreSlim(this.collection.Query().Where(x => x.State == DownloadState.Downloading).Count());
    }

    public IReadOnlyCollection<DownloadJob> AllJobs
    {
        get => this.jobs;
    }

    public async Task<DownloadJob> NextDownloadJob()
    {
        await this.downloadSemaphore.WaitAsync();
        return this.AllJobs.First(x => x.State == DownloadState.Pending || x.State == DownloadState.Downloading);
    }

    public async Task<DownloadJob> NextFetchJob()
    {
        await this.fetchSemaphore.WaitAsync();
        return this.AllJobs.First(x => x.State == DownloadState.Fetching);
    }

    public void EnqueueFetch(string url, MediaFileExtension format, QualityOption quality)
    {
        var job = new DownloadJob(url)
        {
            TargetFormat = format,
            AutomaticQualityOption = quality,
            State = DownloadState.Fetching
        };

        this.jobs.Add(job);

        this.fetchSemaphore.Release();
    }

    public void EnqueueDownload(DownloadJob job)
    {
        job.State = DownloadState.Downloading;

        this.collection.Insert(job);

        this.jobs.Add(job);
        this.downloadSemaphore.Release();
    }
    
    public void UpdateState(int jobId, DownloadState state)
    {
        var job = this.collection.FindById(jobId);

        if (job == null)
        {
            return;
        }

        job.State = state;
        this.collection.Update(job);
    }


    public void Dispose()
    {
        this.jobs.CollectionChanged -= this.OnCollectionChanged;

        this.database.Dispose();
        this.downloadSemaphore.Dispose();
        this.fetchSemaphore.Dispose();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            if (e.NewItems is not IEnumerable<DownloadJob> items)
            {
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    this.collection.Insert(items.First());
                    break;
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }
}