// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Maestro.Data.Helpers;
using Newtonsoft.Json;

namespace Maestro.Data.Models
{
    public class Subscription
    {
        private string _sourceRepository;
        private string _targetRepository;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public int ChannelId { get; set; }

        public Channel Channel { get; set; }

        public string SourceRepository
        {
            get
            {
                return NormalizationOperation.NormalizeUrl(_sourceRepository);
            }

            set
            {
                _sourceRepository = NormalizationOperation.NormalizeUrl(value);
            }
        }

        public string TargetRepository
        {
            get
            {
                return NormalizationOperation.NormalizeUrl(_targetRepository);
            }

            set
            {
                _targetRepository = NormalizationOperation.NormalizeUrl(value);
            }
        }

        public string TargetBranch { get; set; }

        [Column("Policy")]
        public string PolicyString { get; set; }

        public bool Enabled { get; set; } = true;

        [NotMapped]
        public SubscriptionPolicy PolicyObject
        {
            get => PolicyString == null ? null : JsonConvert.DeserializeObject<SubscriptionPolicy>(PolicyString);
            set => PolicyString = value == null ? null : JsonConvert.SerializeObject(value);
        }

        public int? LastAppliedBuildId { get; set; }
        public Build LastAppliedBuild { get; set; }
    }

    public class SubscriptionUpdate
    {
        [Key]
        public Guid SubscriptionId { get; set; }

        public Subscription Subscription { get; set; }

        /// <summary>
        ///     **true** if the update succeeded; **false** otherwise.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        ///     A message describing what the subscription was trying to do.
        ///     e.g. 'Updating dependencies from dotnet/coreclr in dotnet/corefx'
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        ///     The error that occured, if any.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        ///     The method that was called.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        ///     The parameters to the called method.
        /// </summary>
        public string Arguments { get; set; }
    }

    public class SubscriptionUpdateHistory
    {
        [Key]
        public Guid SubscriptionId { get; set; }

        /// <summary>
        ///     **true** if the update succeeded; **false** otherwise.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        ///     A message describing what the subscription was trying to do.
        ///     e.g. 'Updating dependencies from dotnet/coreclr in dotnet/corefx'
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        ///     The error that occured, if any.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        ///     The method that was called.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        ///     The parameters to the called method.
        /// </summary>
        public string Arguments { get; set; }
    }
}
