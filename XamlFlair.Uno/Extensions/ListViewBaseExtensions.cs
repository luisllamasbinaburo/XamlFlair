﻿using System;
using System.Linq;
using System.Reactive.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace XamlFlair.Extensions
{
	internal static class ListViewBaseExtensions
	{
		internal static ILogger Logger;

		internal static void Initialize(this ListViewBase lvb)
		{
			lvb.Loaded += AnimatedListViewBase_Loaded;

			void AnimatedListViewBase_Loaded(object sender, RoutedEventArgs e)
			{
				lvb.Loaded -= AnimatedListViewBase_Loaded;

				// Perform validations on the ListViewBase
				Animations.Validate(lvb);

				// Perform validations on the ListViewBase items
				Animations.ValidateListViewBase(lvb);
			}
		}

		internal static void RegisterListEvents(this ListViewBase lvb, Action itemsSourceChangedAction)
		{
			// Observe the Unloaded of the control (including Error or Completed)
			var unloaded = (lvb as FrameworkElement).Events().Unloaded;

			lvb.Observe(ItemsControl.ItemsSourceProperty)
				.TakeUntil(unloaded)
				// Animate only if the attached property is true 
				.Where(_ => Animations.GetAnimateOnItemsSourceChange(lvb))
				.Subscribe(
				_ => itemsSourceChangedAction?.Invoke(),
				ex => Logger?.LogError($"Error on subscription to changes of the {nameof(ListViewBase.ItemsSource)} property of {nameof(ListViewBase)}", ex));
		}

		internal static void OnApplyTemplateEx(this ListViewBase lvb)
		{
			// VERY IMPORTANT to clear any existing transitions in order to avoid item flickering 
			lvb.ItemContainerTransitions?.Clear();
		}

		internal static void PrepareContainerForItemOverrideEx<TItem>(this ListViewBase lvb, DependencyObject element, Func<(int firstVisibleIndex, int lastVisibleIndex)> getIndicesFunc, ref bool isFirstItemContainerLoaded)
			where TItem : SelectorItem
		{
			// Don't animate using this override if "on loaded" animations is false
			if (!Animations.GetAnimateOnLoad(lvb))
			{
				return;
			}
			
			if (element is TItem item && Animations.GetItems(lvb) is AnimationSettings settings)
			{
				// Wasm listview animations don't work, therefore don't initially hide the item
#if !__WASM__
				// LIMITATION: Currently, for proper item animation handling, item animations
				// MUST include a 'FadeFrom' animation with an Opacity value of 0
				item.Opacity = settings.Opacity;
#endif
				lvb.AnimateItems<TItem>(item, settings, getIndicesFunc, ref isFirstItemContainerLoaded);
			}
		}

		internal static void AnimateItems<TItem>(this ListViewBase lvb, SelectorItem item, AnimationSettings settings, Func<(int firstVisibleIndex, int lastVisibleIndex)> getIndicesFunc, ref bool isFirstItemContainerLoaded)
			where TItem : SelectorItem
		{
			if (!isFirstItemContainerLoaded)
			{
				isFirstItemContainerLoaded = true;

				item.Loaded += OnContainerLoaded;

				// At this point, the index values are all ready to use.
				async void OnContainerLoaded(object sender, RoutedEventArgs _)
				{
					((SelectorItem)sender).Loaded -= OnContainerLoaded;

					try
					{
						await lvb.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
						{
							var (firstVisibleIndex, lastVisibleIndex) = getIndicesFunc();

							AnimateVisibleItems<TItem>(lvb, settings, firstVisibleIndex, lastVisibleIndex);
						});
					}
					catch (Exception ex)
					{
						Logger?.LogError($"Error within OnContainerLoaded when trying to animate ListViewBase items", ex);
					}
				}
			}
		}

		internal static void AnimateVisibleItems<TItem>(this ListViewBase lvb, AnimationSettings settings, int firstVisibleIndex, int lastVisibleIndex)
			where TItem : SelectorItem
		{
			// Make sure to retrieve the GetInterElementDelay value
			var interElementDelay = Animations.GetInterElementDelay(lvb);
			var top = firstVisibleIndex;
			var bottom = lastVisibleIndex;

			for (var index = top; index <= bottom; index++)
			{
				if (lvb.ContainerFromIndex(index) is TItem item)
				{
					AnimateVisibleItem(item, settings, index, interElementDelay);
				}
			}
		}

		private static void AnimateVisibleItem(SelectorItem item, AnimationSettings settings, int index, double interElementDelay)
		{
			// Create a clone of 'settings'
			var itemSettings = new AnimationSettings().ApplyOverrides(settings);

			itemSettings.Delay += index * interElementDelay;

			Animations.RunAnimation(item, settings: itemSettings);
		}
	}
}