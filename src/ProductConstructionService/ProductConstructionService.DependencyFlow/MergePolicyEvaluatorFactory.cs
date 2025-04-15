// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.DependencyFlow;


namespace ProductConstructionService.DependencyFlow;

internal interface IMergePolicyEvaluatorFactory
{
    IMergePolicyEvaluator CreateMergePolicyEvaluator(PullRequestUpdaterId updaterId);
}

internal class MergePolicyEvaluatorFactory : IMergePolicyEvaluatorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public MergePolicyEvaluatorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IMergePolicyEvaluator CreateMergePolicyEvaluator(PullRequestUpdaterId updaterId)
    {
        return ActivatorUtilities.CreateInstance<MergePolicyEvaluator>(_serviceProvider, updaterId);
    }

}
