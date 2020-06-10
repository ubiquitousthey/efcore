// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.SqlServer.Design.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class SqlServerAnnotationCodeGenerator : AnnotationCodeGenerator
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public SqlServerAnnotationCodeGenerator([NotNull] AnnotationCodeGeneratorDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override List<MethodCallCodeFragment> HandleAnnotations(
            IProperty property, List<IAnnotation> annotations)
        {
            var strategy = RemoveAndReturnValue<SqlServerValueGenerationStrategy?>(
                annotations, SqlServerAnnotationNames.ValueGenerationStrategy);
            var seed = RemoveAndReturnValue<int?>(annotations, SqlServerAnnotationNames.IdentitySeed);
            var increment = RemoveAndReturnValue<int?>(annotations, SqlServerAnnotationNames.IdentityIncrement);

            var methodCallCodeFragments = new List<MethodCallCodeFragment>();

            if (strategy == SqlServerValueGenerationStrategy.IdentityColumn)
            {
                methodCallCodeFragments.Add(
                    new MethodCallCodeFragment(nameof(SqlServerPropertyBuilderExtensions.UseIdentityColumn),
                        (seed, increment) switch
                        {
                            (null, null) => Array.Empty<object>(),
                            (_, null) => new object[] { seed },
                            _ => new object[] { seed, increment }
                        }));
            }

            methodCallCodeFragments.AddRange(base.HandleAnnotations(property, annotations));
            return methodCallCodeFragments;

            static T RemoveAndReturnValue<T>(List<IAnnotation> annotations, string annotationName)
            {
                var annotation = annotations.FirstOrDefault(a => a.Name == annotationName);
                if (annotation != null)
                {
                    annotations.Remove(annotation);
                }
                return (T)annotation?.Value;
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override bool IsHandledByConvention(IModel model, IAnnotation annotation)
        {
            Check.NotNull(model, nameof(model));
            Check.NotNull(annotation, nameof(annotation));

            if (annotation.Name == RelationalAnnotationNames.DefaultSchema)
            {
                return string.Equals("dbo", (string)annotation.Value);
            }

            return annotation.Name == SqlServerAnnotationNames.ValueGenerationStrategy
                && (SqlServerValueGenerationStrategy)annotation.Value == SqlServerValueGenerationStrategy.IdentityColumn;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override MethodCallCodeFragment GenerateFluentApi(IKey key, IAnnotation annotation)
            => annotation.Name == SqlServerAnnotationNames.Clustered
                ? (bool)annotation.Value == false
                    ? new MethodCallCodeFragment(nameof(SqlServerIndexBuilderExtensions.IsClustered), false)
                    : new MethodCallCodeFragment(nameof(SqlServerIndexBuilderExtensions.IsClustered))
                : null;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override MethodCallCodeFragment GenerateFluentApi(IIndex index, IAnnotation annotation)
        {
            if (annotation.Name == SqlServerAnnotationNames.Clustered)
            {
                return (bool)annotation.Value == false
                    ? new MethodCallCodeFragment(nameof(SqlServerIndexBuilderExtensions.IsClustered), false)
                    : new MethodCallCodeFragment(nameof(SqlServerIndexBuilderExtensions.IsClustered));
            }

            if (annotation.Name == SqlServerAnnotationNames.Include)
            {
                return new MethodCallCodeFragment(nameof(SqlServerIndexBuilderExtensions.IncludeProperties), annotation.Value);
            }

            if (annotation.Name == SqlServerAnnotationNames.FillFactor)
            {
                return new MethodCallCodeFragment(nameof(SqlServerIndexBuilderExtensions.HasFillFactor), annotation.Value);
            }

            return null;
        }
    }
}
