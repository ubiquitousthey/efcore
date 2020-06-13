// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class DbContextPool<TContext> : IDbContextPool, IDisposable, IAsyncDisposable
        where TContext : DbContext
    {
        private const int DefaultPoolSize = 32;

        private readonly ConcurrentQueue<DbContext> _pool = new ConcurrentQueue<DbContext>();

        private readonly Func<DbContext> _activator;

        private int _maxSize;
        private int _count;

        private DbContextPoolConfigurationSnapshot _configurationSnapshot;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public sealed class Lease : IDisposable, IAsyncDisposable
        {
            private DbContextPool<TContext> _contextPool;

            /// <summary>
            ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
            ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
            ///     any release. You should only use it directly in your code with extreme caution and knowing that
            ///     doing so can result in application failures when updating to a new Entity Framework Core release.
            /// </summary>
            public Lease([NotNull] DbContextPool<TContext> contextPool)
            {
                _contextPool = contextPool;

                Context = (TContext)_contextPool.Rent();
            }

            /// <summary>
            ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
            ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
            ///     any release. You should only use it directly in your code with extreme caution and knowing that
            ///     doing so can result in application failures when updating to a new Entity Framework Core release.
            /// </summary>
            public TContext Context { get; private set; }

            void IDisposable.Dispose()
            {
                if (_contextPool != null)
                {
                    _contextPool.Return(Context);
                    _contextPool = null;
                    Context = null;
                }
            }

            async ValueTask IAsyncDisposable.DisposeAsync()
            {
                if (_contextPool != null)
                {
                    await _contextPool.ReturnAsync(Context).ConfigureAwait(false);
                    _contextPool = null;
                    Context = null;
                }
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public DbContextPool([NotNull] DbContextOptions<TContext> options)
        {
            _maxSize = options.FindExtension<CoreOptionsExtension>()?.MaxPoolSize ?? DefaultPoolSize;

            options.Freeze();

            _activator = CreateActivator(options);

            if (_activator == null)
            {
                throw new InvalidOperationException(
                    CoreStrings.PoolingContextCtorError(typeof(TContext).ShortDisplayName()));
            }
        }

        private static Func<DbContext> CreateActivator(DbContextOptions<TContext> options)
        {
            var constructors
                = typeof(TContext).GetTypeInfo().DeclaredConstructors
                    .Where(c => !c.IsStatic && c.IsPublic)
                    .ToArray();

            if (constructors.Length == 1)
            {
                var parameters = constructors[0].GetParameters();

                if (parameters.Length == 1
                    && (parameters[0].ParameterType == typeof(DbContextOptions)
                        || parameters[0].ParameterType == typeof(DbContextOptions<TContext>)))
                {
                    return
                        Expression.Lambda<Func<TContext>>(
                                Expression.New(constructors[0], Expression.Constant(options)))
                            .Compile();
                }
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DbContext Rent()
        {
            if (_pool.TryDequeue(out var context))
            {
                Interlocked.Decrement(ref _count);

                Check.DebugAssert(_count >= 0, $"_count is {_count}");

                ((IDbContextPoolable)context).Resurrect(_configurationSnapshot);

                return context;
            }

            context = _activator();

            NonCapturingLazyInitializer
                .EnsureInitialized(
                    ref _configurationSnapshot,
                    (IDbContextPoolable)context,
                    c => c.SnapshotConfiguration());

            ((IDbContextPoolable)context).SetPool(this);

            return context;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void ContextDisposed(DbContext context)
        {
            // No-op when not using a standalone pool, since disposal of the service scope triggers release
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected virtual void Return([NotNull] DbContext context)
        {
            if (Interlocked.Increment(ref _count) <= _maxSize)
            {
                ((IDbContextPoolable)context).ResetState();

                _pool.Enqueue(context);
            }
            else
            {
                PooledReturn(context);
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        private async ValueTask ReturnAsync([NotNull] DbContext context)
        {
            if (Interlocked.Increment(ref _count) <= _maxSize)
            {
                await ((IDbContextPoolable)context).ResetStateAsync().ConfigureAwait(false);

                _pool.Enqueue(context);
            }
            else
            {
                PooledReturn(context);
            }
        }

        private void PooledReturn(DbContext context)
        {
            Interlocked.Decrement(ref _count);

            Check.DebugAssert(_maxSize == 0 || _pool.Count <= _maxSize, $"_maxSize is {_maxSize}");

            ((IDbContextPoolable)context).SetPool(null);
            context.Dispose();
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void Dispose()
        {
            _maxSize = 0;

            while (_pool.TryDequeue(out var context))
            {
                ((IDbContextPoolable)context).SetPool(null);
                context.Dispose();
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual async ValueTask DisposeAsync()
        {
            _maxSize = 0;

            while (_pool.TryDequeue(out var context))
            {
                ((IDbContextPoolable)context).SetPool(null);
                await context.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
