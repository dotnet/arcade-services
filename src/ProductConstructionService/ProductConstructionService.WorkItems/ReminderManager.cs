// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public interface IReminderManager<T> where T : WorkItem
{
    Task SetReminderAsync(T reminder, TimeSpan dueTime);

    Task UnsetReminderAsync();

    Task ReminderReceivedAsync();
}

public class ReminderManager<T> : IReminderManager<T> where T : WorkItem
{
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly bool _isCodeFlow;
    private readonly IRedisCache<ReminderArguments> _receiptCache;

    public ReminderManager(
        IWorkItemProducerFactory workItemProducerFactory,
        IRedisCacheFactory cacheFactory,
        string key,
        bool isCodeFlow)
    {
        _workItemProducerFactory = workItemProducerFactory;
        _isCodeFlow = isCodeFlow;
        _receiptCache = cacheFactory.Create<ReminderArguments>($"Reminder_{key}", includeTypeInKey: false);
    }

    public async Task SetReminderAsync(T payload, TimeSpan visibilityTimeout)
    {
        // Check if the updater already has a reminder. If it doesn't, we don't need to add another one
        if ((await _receiptCache.TryGetStateAsync()) == null)
        {
            var client = _workItemProducerFactory.CreateProducer<T>(_isCodeFlow);
            var sendReceipt = await client.ProduceWorkItemAsync(payload, visibilityTimeout);
            await _receiptCache.SetAsync(new ReminderArguments(sendReceipt.PopReceipt, sendReceipt.MessageId), visibilityTimeout);
        }
    }

    public async Task UnsetReminderAsync()
    {
        var receipt = await _receiptCache.TryDeleteAsync();
        if (receipt == null)
        {
            return;
        }

        var client = _workItemProducerFactory.CreateProducer<T>(_isCodeFlow);

        try
        {
            await client.DeleteWorkItemAsync(receipt.MessageId, receipt.PopReceipt);
        }
        catch (RequestFailedException e) when (e.ErrorCode == "MessageNotFound" || e.ErrorCode == "PopReceiptMismatch")
        {
            // The message was already deleted, so we can ignore this exception.
        }
    }

    public async Task ReminderReceivedAsync()
    {
        await _receiptCache.TryDeleteAsync();
    }

    private class ReminderArguments
    {
        public string PopReceipt { get; set; }

        public string MessageId { get; set; }

        public ReminderArguments(string popReceipt, string messageId)
        {
            PopReceipt = popReceipt;
            MessageId = messageId;
        }
    }
}
