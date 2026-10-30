namespace Bpsim.UI
{
	/// <summary>
	/// 顶部工具栏的建造工具。与左侧栏的 <see cref="T:Bpsim.Parts.PartOperationMode" />（放置/擦除/框选）
	/// 是两套独立的模式：工具栏工具针对“已存在的部件”做调整，激活时会让左侧栏进入中性状态
	/// （<see cref="M:Bpsim.UI.PartOperator.SetNeutral" />），因此不会与放置笔刷抢指针，
	/// 同时镜头在工具激活期间不跟随拖动。
	/// <para>
	/// 工具栏只暴露 4 个工具（平移 / 整车平移 / 部件变换 / 载具变换）。曾经并列的
	/// 「部件调整」「旋转部件」「整车旋转」「自由变换」都被合并掉了，数值仍然保留在本枚举里，
	/// 一是让历史引用（旧探针、旧文档、外部脚本）继续编译，二是避免后面再加工具时插队改号。
	/// </para>
	/// </summary>
	public enum BuildTool
	{
		/// <summary>没有激活任何工具。</summary>
		None = 0,

		/// <summary>
		/// 历史值，工具栏不再提供：单击部件打开属性菜单已经由双击手势覆盖
		/// （<see cref="T:Bpsim.UI.PartOperator" /> 订阅了 <c>SceneScreen.DoubleClicked</c>）。
		/// </summary>
		Adjust = 1,

		/// <summary>
		/// 平移部件：按住部件拖动按格吸附移动；原地单击（没有拖动）则按部件自身的旋转步长
		/// （4 向 90° / 8 向 45°）旋转一次 —— 和快速建造里「短按旋转 / 长按拖动」是同一套手感。
		/// </summary>
		Move = 2,

		/// <summary>历史值，工具栏不再提供：旋转已并入 <see cref="Move" />。</summary>
		Rotate = 3,

		/// <summary>
		/// 整车平移：按住空白处拖动，整辆载具按格吸附平移；原地单击则绕载具包围盒中心整车转 90°。
		/// </summary>
		MoveAll = 4,

		/// <summary>历史值，工具栏不再提供：整车旋转已并入 <see cref="MoveAll" />。</summary>
		RotateAll = 5,

		/// <summary>
		/// 部件变换：选中一个部件后显示 gizmo，拖红/绿箭头自由平移、拖圆环连续旋转、
		/// 拖四角/四边手柄拉伸（Shift 保持比例）。偏移/角度/缩放写进
		/// <see cref="T:Bpsim.Parts.PartFreeTransform" />，网格占用表不变。
		/// </summary>
		TransformPart = 6,

		/// <summary>历史值，工具栏不再提供：缩放手柄在 <see cref="TransformPart" /> 里常显，不再单独开按钮。</summary>
		FreeTransformPart = 7,

		/// <summary>
		/// 载具变换：整车包围盒中心显示平移/旋转/等比缩放 gizmo，
		/// 所有部件的自由偏移、连续角与缩放一起烘焙，不重建网格占用表。
		/// </summary>
		TransformMachine = 8
	}
}
