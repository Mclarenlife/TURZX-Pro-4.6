using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Turzx.Desktop
{
    public partial class PromptLibraryWindow : Window
    {
        private readonly ObservableCollection<PromptPreset> _allPresets =
            new ObservableCollection<PromptPreset>();
        private readonly ObservableCollection<PromptPreset> _visiblePresets =
            new ObservableCollection<PromptPreset>();

        public PromptLibraryWindow()
        {
            InitializeComponent();
            AddPresets();
            PresetList.ItemsSource = _visiblePresets;
            CategoryCombo.SelectedIndex = 0;
        }

        public string SelectedPrompt { get; private set; }

        private void AddPresets()
        {
            const string strict = " 严格禁止出现任何文字、字母、数字、百分号、单位、日期、时间、图标、符号、进度条、刻度、曲线、柱状图、波形、数据点、仪表读数或水印。所有容器内部必须保持纯净空白，只生成框架边线、分隔线、留白和少量无含义装饰线，方便后续在主题编辑器中自行添加组件。";

            Add("多功能主题", "黑白精密监控框架", "三段式布局，细白线与圆角空容器",
                "生成一张960×320、严格3:1比例的横向电脑副屏背景。纯黑背景，黑白极简、高对比、精密工业仪表风格。左侧约20%为独立窄栏，中部约35%纵向排列三个宽大的空白横槽，右侧约45%排列两行三列圆角空框，底部再排列一行四个较小空框。边框为柔和白灰细线，分隔线使用稀疏点线，对齐精确、间距统一、留白充足。" + strict);
            Add("多功能主题", "暗色玻璃模块", "低对比磨砂卡片，适配多种组件",
                "设计一张960×320、3:1横向副屏背景。深黑磨砂玻璃底色，低饱和黑白配色。左侧一块纵向大空容器，中间三块长条空容器，右侧两行模块化空卡片。每个容器只有低透明炭灰填充、细白灰描边和柔和内侧高光，圆角统一，整体安静、专业、层次细腻。" + strict);
            Add("多功能主题", "非对称高级留白", "减少框体数量，突出大面积干净空间",
                "制作一张960×320、3:1比例的高级黑白横向屏幕背景。左侧放置窄竖向空框并分成上下两区，中央使用两个一大一小的长横向空框，右侧使用大型圆角外框并以细线划分六个空白区域。以大面积黑色留白为主，线条极细，间距与边距严格统一。" + strict);

            Add("游戏主题", "赛博电竞面板", "青紫霓虹边缘，空白模块化电竞框架",
                "生成一张960×320、3:1比例的赛博电竞副屏背景。深黑背景配合克制的青色与紫色边缘光，采用左右不对称的空白模块布局：左侧大型主框，中间三个横向空框，右侧六个紧凑空卡片。边框带轻微霓虹反射和切角，背景有极淡的电路纹理，但界面区域保持完全空白。" + strict);
            Add("游戏主题", "赛车座舱框架", "碳纤维质感与红色切线，不含仪表读数",
                "创作一张960×320、3:1比例的赛车游戏副屏空白模板。暗黑碳纤维背景，少量暗红色速度切线和金属灰描边。中央保留一个宽大的空白主框，两侧排列对称的小型空框，下方设置三个扁平空容器。构图像高性能赛车座舱，但绝不能生成方向盘、仪表盘内容或任何读数。" + strict);
            Add("游戏主题", "战术行动界面", "军用黑灰绿配色与锐利切角空框",
                "设计一张960×320、3:1比例的战术游戏横向背景。黑色与深军绿色低饱和配色，使用锐利切角、细网格和少量警戒色短线。左侧窄竖框、中部上下三块宽框、右侧规则空白网格，所有容器保持干净无内容，整体像尚未装载数据的战术行动控制面板。" + strict);

            Add("动漫主题", "热血海洋冒险", "海贼王式热血航海氛围，不出现现有角色",
                "生成一张960×320、3:1比例的热血海洋冒险动漫风副屏背景，氛围类似海贼王式的自由航海与伙伴冒险，但不得出现任何现有角色、名称、标志或版权徽记。背景是深蓝海面、远方岛屿轮廓和暖色天空，前景用绳索、木质船舷和金属铆钉构成多个空白框架区域，框内保持纯净，整体明快有活力。" + strict);
            Add("动漫主题", "赛博机甲动画", "机甲装甲分割与蓝色能量边缘",
                "制作一张960×320、3:1比例的原创赛博机甲动漫背景。深色装甲板、蓝白能量边缘和机械分层结构围绕画面四周，中间与右侧预留多个规则空白容器。只表现原创机甲材质与舱体框架，不出现人物、机器人主体、作品名称或标志，框内保持平静且便于叠加监控组件。" + strict);
            Add("动漫主题", "樱花幻想夜景", "柔和日系动画氛围与半透明空框",
                "设计一张960×320、3:1比例的原创日系动画幻想夜景背景。深蓝夜空、少量樱花花瓣、远处柔和城市灯光，使用半透明深色圆角框组成左中右三组留白区域。色彩克制，装饰集中在边缘，中央框体内部清晰空白，不出现人物、文字、标志或已有动漫角色。" + strict);

            Add("极简主题", "纯黑高级极简", "极细灰线与最大化留白",
                "生成一张960×320、3:1比例的纯黑高级极简副屏背景。仅使用极细的深灰和柔白线条划分五到八个空白区域，圆角轻微，完全无纹理、无光晕、无复杂装饰。边距宽松、网格精确、比例协调，呈现高端消费电子系统界面的克制秩序。" + strict);
            Add("极简主题", "浅色纸面网格", "暖白背景与淡灰空框，适合浅色主题",
                "创作一张960×320、3:1比例的浅色极简副屏背景。暖白到浅灰的纯净底色，以极淡灰线和少量深灰细线组成模块化空框；左侧窄栏、中间长条区域、右侧规则网格。无阴影或只保留极轻微层次，整体像精致的工业设计稿。" + strict);
            Add("极简主题", "柔和灰阶卡片", "圆润灰阶层次，无彩色干扰",
                "设计一张960×320、3:1比例的灰阶极简屏幕背景。炭黑底色上排列大小不同的深灰圆角空卡片，卡片边缘只有轻柔明暗变化，没有明显描边和发光。采用稳定的三列布局与统一间距，保留大量干净空白，视觉柔和、现代、耐看。" + strict);

            Add("科幻主题", "深空舰桥 HUD", "深空蓝与细薄HUD切角框架",
                "生成一张960×320、3:1比例的原创深空舰桥HUD背景。黑蓝色太空底色，边缘有非常稀疏的星点和舱壁轮廓，前景由细薄蓝白切角线组成多个空白控制框。左侧纵向框，中部三个水平框，右侧模块网格，科技感精密但不杂乱。" + strict);
            Add("科幻主题", "量子实验室", "冷白发光线与精密实验舱结构",
                "制作一张960×320、3:1比例的未来量子实验室副屏背景。纯黑与冷灰底色，少量冷白和冰蓝边缘光，使用实验舱结构、精密金属接缝和半透明空白容器构成规则布局。光效只沿框架边缘出现，容器内部保持黑色纯净。" + strict);
            Add("科幻主题", "全息网格空框", "克制青色全息层次与悬浮容器",
                "设计一张960×320、3:1比例的未来全息界面空白背景。深黑空间中排列七个半透明悬浮空框，使用低亮度青色细线、少量定位短线和极淡透视网格营造空间感。结构左右平衡，边缘锐利，光晕克制，所有框内无任何信息。" + strict);
        }

        private void Add(string category, string name, string summary, string prompt)
        {
            _allPresets.Add(new PromptPreset
            {
                Category = category,
                Name = name,
                Summary = summary,
                Prompt = prompt
            });
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryCombo == null || PresetList == null) return;
            ComboBoxItem selected = CategoryCombo.SelectedItem as ComboBoxItem;
            string category = selected == null ? null : Convert.ToString(selected.Tag);
            _visiblePresets.Clear();
            foreach (PromptPreset preset in _allPresets)
                if (string.Equals(preset.Category, category, StringComparison.Ordinal))
                    _visiblePresets.Add(preset);
            PresetList.SelectedIndex = _visiblePresets.Count > 0 ? 0 : -1;
        }

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PromptPreset preset = PresetList.SelectedItem as PromptPreset;
            if (preset == null) return;
            PresetTitle.Text = preset.Name;
            PromptEditor.Text = preset.Prompt;
        }

        private void LoadPrompt_Click(object sender, RoutedEventArgs e)
        {
            string prompt = (PromptEditor.Text ?? string.Empty).Trim();
            if (prompt.Length == 0) return;
            SelectedPrompt = prompt;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    internal sealed class PromptPreset
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Prompt { get; set; }
    }
}
