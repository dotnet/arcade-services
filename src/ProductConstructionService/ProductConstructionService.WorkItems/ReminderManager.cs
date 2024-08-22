// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public interface IReminderManager<T> where T : WorkItem
{
    Task RegisterReminderAsync(T reminder, TimeSpan visibilityTimeout);

    Task UnregisterReminderAsync();
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
        _receiptCache = cacheFactory.Create<ReminderArguments>($"ReminderReceipt_{key}");
    }

    public async Task RegisterReminderAsync(T payload, TimeSpan visibilityTimeout)
    {
        var client = _workItemProducerFactory.CreateProducer<T>();
        var sendReceipt = await client.ProduceWorkItemAsync(payload, visibilityTimeout);
        await _receiptCache.SetAsync(new ReminderArguments(sendReceipt.PopReceipt, sendReceipt.MessageId), visibilityTimeout);
    }

    public async Task UnregisterReminderAsync()
    {
        var receipt = await _receiptCache.TryDelete();
        if (receipt == null)
        {
            return;
        }

        var client = _workItemProducerFactory.CreateProducer<T>();
        await client.DeleteWorkItemAsync(receipt.MessageId, receipt.PopReceipt);
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
