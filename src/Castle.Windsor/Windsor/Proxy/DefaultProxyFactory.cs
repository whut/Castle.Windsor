// Copyright 2004-2011 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Castle.Windsor.Proxy
{
	using System;
	using System.Linq;
	using System.Runtime.Serialization;

	using Castle.Core;
	using Castle.Core.Interceptor;
	using Castle.Core.Internal;
	using Castle.DynamicProxy;
	using Castle.MicroKernel;
	using Castle.MicroKernel.Context;
	using Castle.MicroKernel.Proxy;

#if (SILVERLIGHT)
	using Castle.DynamicProxy.SilverlightExtensions;
#endif

	/// <summary>
	///   This implementation of <see cref = "IProxyFactory" /> relies 
	///   on DynamicProxy to expose proxy capabilities.
	/// </summary>
	/// <remarks>
	///   Note that only virtual methods can be intercepted in a 
	///   concrete class. However, if the component 
	///   was registered with a service interface, we proxy
	///   the interface and the methods don't need to be virtual,
	/// </remarks>
	[Serializable]
#if (SILVERLIGHT)
	public class DefaultProxyFactory : AbstractProxyFactory
#else
	public class DefaultProxyFactory : AbstractProxyFactory, IDeserializationCallback
#endif
	{
		[NonSerialized]
		protected ProxyGenerator generator;

		/// <summary>
		///   Constructs a DefaultProxyFactory
		/// </summary>
		public DefaultProxyFactory()
		{
			Init();
		}

		public override object Create(IProxyFactoryExtension customFactory, IKernel kernel, ComponentModel model, CreationContext context,
		                              params object[] constructorArguments)
		{
			var interceptors = ObtainInterceptors(kernel, model, context);
			var proxyOptions = ProxyUtil.ObtainProxyOptions(model, true);
			var proxyGenOptions = CreateProxyGenerationOptionsFrom(proxyOptions, kernel, context, model);

			CustomizeOptions(proxyGenOptions, kernel, model, constructorArguments);
			var builder = generator.ProxyBuilder;
			var proxy = customFactory.Generate(builder, proxyGenOptions, interceptors);

			CustomizeProxy(proxy, proxyGenOptions, kernel, model);
			ReleaseHook(proxyGenOptions, kernel);
			return proxy;
		}

		private void ReleaseHook(ProxyGenerationOptions proxyGenOptions, IKernel kernel)
		{
			if (proxyGenOptions.Hook == null)
			{
				return;
			}
			kernel.ReleaseComponent(proxyGenOptions.Hook);
		}

		/// <summary>
		///   Creates the proxy for the supplied component.
		/// </summary>
		/// <param name = "kernel">The kernel.</param>
		/// <param name = "target">The target.</param>
		/// <param name = "model">The model.</param>
		/// <param name = "constructorArguments">The constructor arguments.</param>
		/// <param name = "context">The creation context</param>
		/// <returns>The component proxy.</returns>
		public override object Create(IKernel kernel, object target, ComponentModel model, CreationContext context, params object[] constructorArguments)
		{
			object proxy;

			var interceptors = ObtainInterceptors(kernel, model, context);
			var proxyOptions = ProxyUtil.ObtainProxyOptions(model, true);
			var proxyGenOptions = CreateProxyGenerationOptionsFrom(proxyOptions, kernel, context, model);

			CustomizeOptions(proxyGenOptions, kernel, model, constructorArguments);

			var interfaces = proxyOptions.AdditionalInterfaces;
			if (model.HasClassServices == false)
			{
				var firstService = model.InterfaceServices.First();
				var additionalInterfaces = model.InterfaceServices.Skip(1).Concat(interfaces).ToArray();
				if (proxyOptions.OmitTarget)
				{
					proxy = generator.CreateInterfaceProxyWithoutTarget(firstService, additionalInterfaces, proxyGenOptions, interceptors);
				}
				else if (proxyOptions.AllowChangeTarget)
				{
					proxy = generator.CreateInterfaceProxyWithTargetInterface(firstService, additionalInterfaces, target, proxyGenOptions, interceptors);
				}
				else
				{
					proxy = generator.CreateInterfaceProxyWithTarget(firstService, additionalInterfaces, target, proxyGenOptions, interceptors);
				}
			}
			else
			{
				Type classToProxy;
				if (model.Implementation != null && model.Implementation != typeof(LateBoundComponent))
				{
					classToProxy = model.Implementation;
				}
				else
				{
					classToProxy = model.AllServices.First();
				}
				var additionalInterfaces = model.InterfaceServices
					.Concat(interfaces)
					.ToArray();
				proxy = generator.CreateClassProxy(classToProxy, additionalInterfaces, proxyGenOptions, constructorArguments, interceptors);
			}

			CustomizeProxy(proxy, proxyGenOptions, kernel, model);
			ReleaseHook(proxyGenOptions, kernel);
			return proxy;
		}

		protected static ProxyGenerationOptions CreateProxyGenerationOptionsFrom(ProxyOptions proxyOptions, IKernel kernel, CreationContext context,
		                                                                         ComponentModel model)
		{
			var proxyGenOptions = new ProxyGenerationOptions();
			if (proxyOptions.Hook != null)
			{
				var hook = proxyOptions.Hook.Resolve(kernel, context);
				if (hook != null && hook is IOnBehalfAware)
				{
					((IOnBehalfAware)hook).SetInterceptedComponentModel(model);
				}
				proxyGenOptions.Hook = hook;
			}

			if (proxyOptions.Selector != null)
			{
				var selector = proxyOptions.Selector.Resolve(kernel, context);
				if (selector != null && selector is IOnBehalfAware)
				{
					((IOnBehalfAware)selector).SetInterceptedComponentModel(model);
				}
				proxyGenOptions.Selector = selector;
			}
#if (!SILVERLIGHT)
			if (proxyOptions.UseMarshalByRefAsBaseClass)
			{
				proxyGenOptions.BaseTypeForInterfaceProxy = typeof(MarshalByRefObject);
			}
#endif
			foreach (var mixInReference in proxyOptions.MixIns)
			{
				var mixIn = mixInReference.Resolve(kernel, context);
				proxyGenOptions.AddMixinInstance(mixIn);
			}

			return proxyGenOptions;
		}

		protected virtual void CustomizeProxy(object proxy, ProxyGenerationOptions options, IKernel kernel, ComponentModel model)
		{
		}

		protected virtual void CustomizeOptions(ProxyGenerationOptions options, IKernel kernel, ComponentModel model, object[] arguments)
		{
		}

		/// <summary>
		///   Determines if the component requires a target instance for proxying.
		/// </summary>
		/// <param name = "kernel">The kernel.</param>
		/// <param name = "model">The model.</param>
		/// <returns>true if an instance is required.</returns>
		public override bool RequiresTargetInstance(IKernel kernel, ComponentModel model)
		{
			var proxyOptions = ProxyUtil.ObtainProxyOptions(model, true);

			return model.HasClassServices == false &&
			       proxyOptions.OmitTarget == false;
		}

#if !SILVERLIGHT
		public void OnDeserialization(object sender)
		{
			Init();
		}
#endif

		private void Init()
		{
			generator = new ProxyGenerator();
		}
	}
}