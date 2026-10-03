// 参考来源： https://tyrrrz.me/Blog/WPF-TreeView-SelectedItem-TwoWay-binding

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace FileConverter.Views
{
    public class TreeViewSelectionBehavior : Behavior<TreeView>
    {
        public delegate bool IsChildOfPredicate(object nodeA, object nodeB);

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object),
                typeof(TreeViewSelectionBehavior),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));

        public static readonly DependencyProperty HierarchyPredicateProperty =
            DependencyProperty.Register(nameof(HierarchyPredicate), typeof(IsChildOfPredicate),
                typeof(TreeViewSelectionBehavior),
                new FrameworkPropertyMetadata(null));

        public static readonly DependencyProperty ExpandSelectedProperty =
            DependencyProperty.Register(nameof(ExpandSelected), typeof(bool),
                typeof(TreeViewSelectionBehavior),
                new FrameworkPropertyMetadata(false));

        private readonly EventSetter treeViewItemEventSetter;
        private bool modelHandled;

        public TreeViewSelectionBehavior()
        {
            this.treeViewItemEventSetter = new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(this.OnTreeViewItemLoaded));
        }

        // 可绑定的选中项。
        public object SelectedItem
        {
            get => this.GetValue(SelectedItemProperty);
            set => this.SetValue(SelectedItemProperty, value);
        }

        // 判断两个项是否存在层级关系。
        public IsChildOfPredicate HierarchyPredicate
        {
            get => (IsChildOfPredicate)this.GetValue(HierarchyPredicateProperty);
            set => this.SetValue(HierarchyPredicateProperty, value);
        }

        // 是否展开选中项。
        public bool ExpandSelected
        {
            get => (bool)this.GetValue(ExpandSelectedProperty);
            set => this.SetValue(ExpandSelectedProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            this.AssociatedObject.SelectedItemChanged += this.OnTreeViewSelectedItemChanged;
            ((INotifyCollectionChanged)this.AssociatedObject.Items).CollectionChanged += this.OnTreeViewItemsChanged;

            this.UpdateTreeViewItemStyle();
            this.modelHandled = true;
            this.UpdateAllTreeViewItems();
            this.modelHandled = false;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

            if (this.AssociatedObject != null)
            {
                this.AssociatedObject.ItemContainerStyle?.Setters?.Remove(this.treeViewItemEventSetter);
                this.AssociatedObject.SelectedItemChanged -= this.OnTreeViewSelectedItemChanged;
                ((INotifyCollectionChanged)this.AssociatedObject.Items).CollectionChanged -= this.OnTreeViewItemsChanged;
            }
        }

        private static void OnSelectedItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var behavior = (TreeViewSelectionBehavior)sender;
            if (behavior.modelHandled)
            {
                return;
            }

            if (behavior.AssociatedObject == null)
            {
                return;
            }

            behavior.modelHandled = true;
            behavior.UpdateAllTreeViewItems();
            behavior.modelHandled = false;
        }

        // 从指定项开始更新状态，并按需递归。
        private void UpdateTreeViewItem(TreeViewItem item, bool recurse)
        {
            if (this.SelectedItem == null)
            {
                return;
            }

            var model = item.DataContext;

            // 当前模型对应选中项时，设置选中状态并返回。
            if (this.SelectedItem == model && !item.IsSelected)
            {
                item.IsSelected = true;
            }
            // 当前模型是选中项的父级时展开。
            else
            {
                bool isParentOfModel = this.HierarchyPredicate?.Invoke(this.SelectedItem, model) ?? true;
                if (isParentOfModel)
                {
                    item.IsExpanded = true;
                }
            }

            if (item.IsSelected && this.ExpandSelected)
            {
                item.IsExpanded = true;
            }

            // 递归处理子项。
            if (recurse)
            {
                foreach (var subitem in item.Items)
                {
                    if (item.ItemContainerGenerator.ContainerFromItem(subitem) is TreeViewItem tvi)
                    {
                        this.UpdateTreeViewItem(tvi, true);
                    }
                }
            }
        }

        // 更新所有项的状态。
        private void UpdateAllTreeViewItems()
        {
            var treeView = this.AssociatedObject;
            foreach (var item in treeView.Items)
            {
                if (treeView.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                {
                    this.UpdateTreeViewItem(tvi, true);
                }
            }
        }

        // 向 ItemContainerStyle 添加 Loaded 事件处理程序。
        private void UpdateTreeViewItemStyle()
        {
            if (this.AssociatedObject.ItemContainerStyle == null)
            {
                this.AssociatedObject.ItemContainerStyle = new Style(
                    typeof(TreeViewItem),
                    Application.Current.TryFindResource(typeof(TreeViewItem)) as Style);
            }

            if (!this.AssociatedObject.ItemContainerStyle.Setters.Contains(this.treeViewItemEventSetter))
            {
                this.AssociatedObject.ItemContainerStyle.Setters.Add(this.treeViewItemEventSetter);
            }
        }

        private void OnTreeViewItemsChanged(object sender,
            NotifyCollectionChangedEventArgs args)
        {
            this.UpdateAllTreeViewItems();
        }

        private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
        {
            if (this.modelHandled)
            {
                return;
            }
            if (this.AssociatedObject.Items.SourceCollection == null)
            {
                return;
            }

            this.SelectedItem = args.NewValue;
        }

        private void OnTreeViewItemLoaded(object sender, RoutedEventArgs args)
        {
            this.UpdateTreeViewItem((TreeViewItem)sender, false);
        }
    }
}
