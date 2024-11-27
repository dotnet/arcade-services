// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public interface IReminderManager<T> where T : WorkItem
{
    Task SetReminderAsync(T reminder, TimeSpan dueTime, bool isCodeFlow);

    Task UnsetReminderAsync(bool isCodeFlow);

    Task ReminderReceivedAsync();
}

public class ReminderManager<T> : IReminderManager<T> where T : WorkItem
{
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly IRedisCache<ReminderArguments> _receiptCache;

    public ReminderManager(
        IWorkItemProducerFactory workItemProducerFactory,
        IRedisCacheFactory cacheFactory,
        string key)
    {
        _workItemProducerFactory = workItemProducerFactory;
        _receiptCache = cacheFactory.Create<ReminderArguments>($"Reminder_{key}", includeTypeInKey: false);
    }

    public async Task SetReminderAsync(T payload, TimeSpan visibilityTimeout, bool isCodeFlow)
    {
        var client = _workItemProducerFactory.CreateProducer<T>(isCodeFlow);
        var sendReceipt = await client.ProduceWorkItemAsync(payload, visibilityTimeout);
        await _receiptCache.SetAsync(new ReminderArguments(sendReceipt.PopReceipt, sendReceipt.MessageId), visibilityTimeout + TimeSpan.FromHours(4));
    }

    public async Task UnsetReminderAsync(bool isCodeFlow)
    {
        var receipt = await _receiptCache.TryDeleteAsync();
        if (receipt == null)
        {
            return;
        }

        var client = _workItemProducerFactory.CreateProducer<T>(isCodeFlow);

        try
        {
            await client.DeleteWorkItemAsync(receipt.MessageId, receipt.PopReceipt);
        }
        catch (RequestFailedException e) when (e.Message.Contains("The specified message does not exist")
                                            || e.Message.Contains("did not match the pop receipt"))
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
