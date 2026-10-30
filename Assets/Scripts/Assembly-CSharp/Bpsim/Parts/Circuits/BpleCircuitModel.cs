using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System;

namespace Bpsim.Parts.Circuits.Model
{
// Ported from the adjacent BPLE project; keep solver semantics and pooled scratch buffers.
internal static class ElectricalSystem { public const bool EnableMultithreadCircuit = false; }
public class CircuitSimulator
{
	private class CircuitEquationSolver
	{
		private int m_equationCount;

		private int m_variableCount;

		private int m_additionalVariableCount;

		private DisjointSet m_disjointSet;

		private int[] m_variableIndexes;

		private double[][] m_augmentedMatrix;

		private VariableValue[] m_variables;

		public VariableValue GetVariable(int index)
		{
			int num = m_variableIndexes[index];
			if (num == -1)
			{
				return new VariableValue(0.0, isConstant: true, isSolved: true);
			}
			return m_variables[num];
		}

		public VariableValue GetAdditionalVariable(int index)
		{
			return m_variables[m_variableCount + index];
		}

		public CircuitEquationSolver()
		{
			m_variableIndexes = Array.Empty<int>();
			m_augmentedMatrix = Array.Empty<double[]>();
			m_variables = Array.Empty<VariableValue>();
		}

		public void Solve(List<Node> nodes, Graph<Branch> nodeGraph, int additionalVariableCount)
		{
			Initialize(nodes, nodeGraph, additionalVariableCount);
			CreateEquations(nodes, nodeGraph);
			SolveEquations();
		}

		public void Clear()
		{
			Array.Clear(m_variableIndexes, 0, m_variableIndexes.Length);
			Array.Clear(m_variables, 0, m_variables.Length);
			double[][] augmentedMatrix = m_augmentedMatrix;
			foreach (double[] array in augmentedMatrix)
			{
				Array.Clear(array, 0, array.Length);
			}
		}

		private void Initialize(List<Node> nodes, Graph<Branch> nodeGraph, int additionalVariableCount)
		{
			int count = nodes.Count;
			m_disjointSet = new DisjointSet(count + 1);
			foreach (Node node in nodes)
			{
				foreach (Graph<Branch>.Edge edge in nodeGraph.GetEdges(node.Index))
				{
					Branch value = edge.Value;
					if (value.IsShortCircuit())
					{
						switch (value.Type)
						{
						case BranchType.Common:
							m_disjointSet.Union(node.Index, edge.To);
							break;
						case BranchType.Grounded:
							m_disjointSet.Union(node.Index, count);
							break;
						}
					}
				}
			}
			if (m_variableIndexes.Length < count + 1)
			{
				m_variableIndexes = new int[count + 1];
			}
			m_disjointSet.GetComponentIndexes(m_variableIndexes, out var componentCount);
			int num = m_variableIndexes[count];
			for (int i = 0; i < count; i++)
			{
				int num2 = m_variableIndexes[i];
				if (num2 > num)
				{
					m_variableIndexes[i] = num2 - 1;
				}
				else if (num2 == num)
				{
					m_variableIndexes[i] = -1;
				}
			}
			m_equationCount = count;
			m_variableCount = componentCount - 1;
			m_additionalVariableCount = additionalVariableCount;
		}

		private void CreateEquations(List<Node> nodes, Graph<Branch> nodeGraph)
		{
			double[][] array = m_augmentedMatrix;
			int equationCount = m_equationCount;
			int num = m_variableCount + m_additionalVariableCount;
			if (equationCount > array.Length || (array.Length != 0 && num + 1 > array[0].Length))
			{
				array = new double[equationCount][];
				for (int i = 0; i < equationCount; i++)
				{
					array[i] = new double[num + 1];
				}
				m_augmentedMatrix = array;
			}
			foreach (Node node in nodes)
			{
				double[] array2 = array[node.Index];
				foreach (Graph<Branch>.Edge edge in nodeGraph.GetEdges(node.Index))
				{
					Branch value = edge.Value;
					bool num2 = value.StartElement == node.ElementIndex;
					int num3 = value.AdditionalVariableIndex;
					if (num3 != -1)
					{
						num3 += m_variableCount;
					}
					int num4 = m_variableIndexes[node.Index];
					int num5 = ((edge.To != -1) ? m_variableIndexes[edge.To] : (-1));
					double num6 = 1.0;
					double num7 = value.InvR;
					if (!num2)
					{
						int num8 = num4;
						num4 = num5;
						num5 = num8;
						num6 = 0.0 - num6;
						num7 = 0.0 - num7;
					}
					BranchType type = value.Type;
					if (type != BranchType.Common && type != BranchType.Grounded)
					{
						continue;
					}
					if (num3 != -1)
					{
						array2[num3] += num6;
						continue;
					}
					if (num4 != -1)
					{
						array2[num4] += num7;
					}
					if (num5 != -1)
					{
						array2[num5] -= num7;
					}
					array2[num] -= value.U * num7;
				}
			}
		}

		private void SolveEquations()
		{
			double[][] augmentedMatrix = m_augmentedMatrix;
			int equationCount = m_equationCount;
			int num = m_variableCount + m_additionalVariableCount;
			if (num > m_variables.Length)
			{
				m_variables = new VariableValue[num];
			}
			int num2 = 0;
			int i = 0;
			while (num2 < equationCount)
			{
				int num3 = num2;
				double num4 = augmentedMatrix[num2][i];
				num4 = ((num4 >= 0.0) ? num4 : (0.0 - num4));
				for (; i < num; i++)
				{
					for (int j = num2 + 1; j < equationCount; j++)
					{
						double num5 = augmentedMatrix[j][i];
						num5 = ((num5 >= 0.0) ? num5 : (0.0 - num5));
						if (num5 > num4)
						{
							num3 = j;
							num4 = num5;
						}
					}
					if (num4 > 1E-08)
					{
						break;
					}
				}
				if (i == num)
				{
					break;
				}
				if (num3 != num2)
				{
					double[] array = augmentedMatrix[num2];
					augmentedMatrix[num2] = augmentedMatrix[num3];
					augmentedMatrix[num3] = array;
				}
				double num6 = augmentedMatrix[num2][i];
				for (int k = num2 + 1; k < equationCount; k++)
				{
					double num7 = augmentedMatrix[k][i] / num6;
					augmentedMatrix[k][i] = 0.0;
					for (int l = i + 1; l < num + 1; l++)
					{
						augmentedMatrix[k][l] -= augmentedMatrix[num2][l] * num7;
					}
				}
				num2++;
				i++;
			}
			for (int num8 = equationCount - 1; num8 >= 0; num8--)
			{
				int num9 = -1;
				double num10 = 0.0;
				bool flag = true;
				double num11 = augmentedMatrix[num8][num];
				for (int m = num8; m < num; m++)
				{
					double num12 = augmentedMatrix[num8][m];
					if (!(-1E-08 <= num12) || !(num12 <= 1E-08))
					{
						ref VariableValue reference = ref m_variables[m];
						if (reference.IsSolved)
						{
							num11 -= num12 * reference.Value;
							flag &= reference.IsConstant;
						}
						else if (num9 != -1)
						{
							reference.IsConstant = m >= m_variableCount;
							reference.IsSolved = true;
							flag &= reference.IsConstant;
						}
						else
						{
							num9 = m;
							num10 = num12;
						}
					}
				}
				if (num9 != -1)
				{
					m_variables[num9] = new VariableValue(num11 / num10, flag, isSolved: true);
				}
			}
			for (int n = 0; n < num; n++)
			{
				if (!m_variables[n].IsSolved)
				{
					m_variables[n] = new VariableValue(0.0, isConstant: false, isSolved: true);
				}
			}
		}
	}

	private struct Node
	{
		public CircuitElement Element;

		public int Index;

		public double Potential;

		public bool IsGrounded;

		public bool[] Visited;

		private static readonly Node s_empty = new Node(null, -1);

		public bool IsEmpty => Index == -1;

		public int ElementIndex => Element.ElementIndex;

		public static Node Empty => s_empty;

		public Node(CircuitElement element, int index)
		{
			Element = element;
			Index = index;
			Potential = 0.0;
			IsGrounded = false;
			Visited = null;
		}
	}

	private struct Branch
	{
		public BranchType Type;

		public double U;

		public double R;

		public double InvR;

		public int StartElement;

		public int StartElectrode;

		public int EndElement;

		public int EndElectrode;

		public int AdditionalVariableIndex;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsShortCircuit()
		{
			return R == 0.0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOpenCircuit()
		{
			return double.IsPositiveInfinity(R);
		}

		public Branch(int startElement, int startElectrode, int endElement, int endElectrode)
			: this(BranchType.Unknown, 0.0, 0.0, 0.0, startElement, startElectrode, endElement, endElectrode)
		{
		}

		public Branch(BranchType type, double u, double r, double invR, int startElement, int startElectrode, int endElement, int endElectrode)
		{
			Type = type;
			U = u;
			R = r;
			InvR = invR;
			StartElement = startElement;
			StartElectrode = startElectrode;
			EndElement = endElement;
			EndElectrode = endElectrode;
			AdditionalVariableIndex = -1;
		}
	}

	private struct VariableValue
	{
		public double Value;

		public bool IsConstant;

		public bool IsSolved;

		public VariableValue(double value, bool isConstant, bool isSolved)
		{
			Value = value;
			IsConstant = isConstant;
			IsSolved = isSolved;
		}
	}

	private enum BranchType
	{
		Unknown = 0,
		Common = 1,
		Cyclic = 2,
		Grounded = 3,
		Floating = 4
	}

	private int m_frame;

	private double m_deltaTime;

	private List<Node> m_nodes;

	private Dictionary<CircuitElement, Node> m_nodeMap;

	private Graph<Branch> m_nodeGraph;

	private CircuitEquationSolver m_equationSolver;

	private List<KeyValuePair<CircuitElement, SimulationResult>> m_pendingUpdates;

	public int Frame => m_frame;

	public double DeltaTime => m_deltaTime;

	public CircuitSimulator(double deltaTime)
	{
		m_frame = -1;
		m_deltaTime = deltaTime;
		m_nodes = new List<Node>();
		m_nodeMap = new Dictionary<CircuitElement, Node>();
		m_equationSolver = new CircuitEquationSolver();
		m_pendingUpdates = new List<KeyValuePair<CircuitElement, SimulationResult>>();
	}

	public void SetDeltaTime(double deltaTime)
	{
		m_deltaTime = deltaTime;
	}

	public virtual void Clear()
	{
		m_nodes.Clear();
		m_nodeMap.Clear();
		m_equationSolver.Clear();
	}

	public virtual void Simulate(List<CircuitElement> elements)
	{
		m_frame++;
		int count = elements.Count;
		DisjointSet disjointSet = new DisjointSet(count);
		for (int i = 0; i < count; i++)
		{
			elements[i].ElementIndex = i;
		}
		foreach (CircuitElement element in elements)
		{
			foreach (Electrode electrode in element.Electrodes)
			{
				if (electrode.IsConnected)
				{
					disjointSet.Union(element.ElementIndex, electrode.ConnectedElement.ElementIndex);
				}
			}
		}
		int num = 0;
		int[] size;
		int componentCount;
		int[] components = disjointSet.GetComponents(out size, out componentCount);
		List<CircuitElement>[] componentLists = new List<CircuitElement>[componentCount];
		for (int j = 0; j < componentCount; j++)
		{
			List<CircuitElement> list = new List<CircuitElement>();
			for (int k = 0; k < size[j]; k++)
			{
				CircuitElement circuitElement = elements[components[num + k]];
				circuitElement.CircuitIndex = j;
				list.Add(circuitElement);
			}
			num += size[j];
			componentLists[j] = list;
		}
		CircuitSimulator[] simulators = new CircuitSimulator[componentCount];
		if (ElectricalSystem.EnableMultithreadCircuit && componentCount > 1)
		{
			Parallel.For(0, componentCount, j =>
			{
				simulators[j] = new CircuitSimulator(m_deltaTime);
				simulators[j].SimulateCircuit(componentLists[j]);
			});
		}
		else
		{
			for (int j = 0; j < componentCount; j++)
			{
				simulators[j] = this;
				SimulateCircuit(componentLists[j]);
				Clear();
			}
		}
		foreach (CircuitSimulator simulator in simulators)
		{
			if (simulator != null)
			{
				simulator.FlushUpdates(this);
			}
		}
		foreach (CircuitElement element2 in elements)
		{
			element2.Update();
		}
	}

	private void SimulateCircuit(List<CircuitElement> elements)
	{
		int num = 0;
		foreach (CircuitElement element in elements)
		{
			if (element.IsNode())
			{
				Node node = new Node(element, num);
				node.Visited = new bool[element.Electrodes.Count];
				m_nodes.Add(node);
				m_nodeMap.Add(element, node);
				num++;
			}
		}
		if (num == 0)
		{
			ScanSingleBranch(elements);
			return;
		}
		m_nodeGraph = new Graph<Branch>(num);
		int num2 = 0;
		foreach (Node node2 in m_nodes)
		{
			List<Electrode> electrodes = node2.Element.Electrodes;
			for (int i = 0; i < electrodes.Count; i++)
			{
				if (electrodes[i].IsConnected && !node2.Visited[i])
				{
					ScanBranch(node2, i, out var end, out var branch);
					if ((branch.Type == BranchType.Common || branch.Type == BranchType.Grounded) && branch.IsShortCircuit())
					{
						branch.AdditionalVariableIndex = num2;
						num2++;
					}
					if (end.Index == -1 || branch.Type == BranchType.Cyclic)
					{
						m_nodeGraph.AddDirectedEdge(node2.Index, end.Index, branch);
					}
					else
					{
						m_nodeGraph.AddUndirectedEdge(node2.Index, end.Index, branch);
					}
				}
			}
		}
		m_equationSolver.Solve(m_nodes, m_nodeGraph, num2);
		ProcessResults();
	}

	private void ScanSingleBranch(List<CircuitElement> elements)
	{
		if (elements.Count <= 1)
		{
			return;
		}
		CircuitElement circuitElement = elements[0];
		foreach (CircuitElement element in elements)
		{
			if (element.GetConnectedElectrodeCount() <= 1)
			{
				circuitElement = element;
			}
		}
		Node start = new Node(circuitElement, -1);
		int startElectrode = -1;
		for (int i = 0; i < circuitElement.Electrodes.Count; i++)
		{
			if (circuitElement.Electrodes[i].IsConnected)
			{
				startElectrode = i;
				break;
			}
		}
		ScanBranch(start, startElectrode, out var _, out var branch);
		bool flag = circuitElement is Ground || circuitElement is Vcc;
		bool flag2 = branch.Type == BranchType.Grounded;
		start.IsGrounded = flag || flag2;
		if (!flag && flag2)
		{
			start.Potential = 0.0 - branch.U;
		}
		if (circuitElement.GetConnectedElectrodeCount() <= 1 && !flag)
		{
			branch.Type = BranchType.Floating;
		}
		ProcessBranchResults(start, Node.Empty, branch);
	}

	private void ScanBranch(Node start, int startElectrode, out Node end, out Branch branch)
	{
		end = Node.Empty;
		branch = new Branch(start.ElementIndex, startElectrode, -1, -1);
		CircuitElement element = start.Element;
		Electrode electrode = element.Electrodes[startElectrode];
		if (!start.IsEmpty)
		{
			start.Visited[startElectrode] = true;
		}
		ScanElement(element, electrode, direction: true, ref branch);
		if (element.GetConnectedElectrodeCount() == 0)
		{
			return;
		}
		electrode = electrode.ConnectedElectrode;
		element = electrode.Element;
		int num = 0;
		while (true)
		{
			if (element == start.Element)
			{
				branch.Type = BranchType.Cyclic;
				break;
			}
			ScanElement(element, electrode, direction: false, ref branch);
			int connectedElectrodeCount = element.GetConnectedElectrodeCount();
			if (connectedElectrodeCount == 0 || connectedElectrodeCount == 1)
			{
				if (element is Ground || element is Vcc)
				{
					branch.Type = BranchType.Grounded;
				}
				else
				{
					branch.Type = BranchType.Floating;
				}
				break;
			}
			if (connectedElectrodeCount >= 3)
			{
				branch.Type = BranchType.Common;
				break;
			}
			electrode = element.FindNextConnectedElectrode(electrode).ConnectedElectrode;
			element = electrode.Element;
			if (num++ > 10000)
			{
				throw new OverflowException("Infinite loops");
			}
		}
		branch.InvR = 1.0 / branch.R;
		if (element != null)
		{
			int electrodeIndex = element.GetElectrodeIndex(electrode);
			branch.EndElement = element.ElementIndex;
			branch.EndElectrode = electrodeIndex;
			if (element.IsNode())
			{
				end = m_nodeMap[element];
				end.Visited[electrodeIndex] = true;
			}
		}
	}

	private void ScanElement(CircuitElement element, Electrode electrode, bool direction, ref Branch branch)
	{
		ScanElement(element, electrode, direction, out var U, out var R);
		branch.U += U;
		branch.R += R;
	}

	private void ScanElement(CircuitElement element, Electrode electrode, bool direction, out double U, out double R)
	{
		U = 0.0;
		R = 0.0;
		if (element is Resistor resistor)
		{
			R = resistor.Resistance;
		}
		else if (element is VoltageSource voltageSource)
		{
			bool flag = (electrode == voltageSource.Cathode) ^ direction;
			U = voltageSource.Voltage * (flag ? 1.0 : (-1.0));
			R = voltageSource.Resistance;
		}
		else if (element is Capacitor capacitor)
		{
			bool flag2 = (electrode == capacitor.Cathode) ^ direction;
			U = capacitor.Charge / capacitor.Capacitance * (flag2 ? 1.0 : (-1.0));
			R = capacitor.Resistance;
		}
		else if (element is Inductor inductor)
		{
			bool flag3 = (electrode == inductor.Cathode) ^ direction;
			double num = (0.0 - inductor.MagneticFlux) / inductor.Inductance;
			U = num * inductor.Resistance * (flag3 ? 1.0 : (-1.0));
			R = inductor.Resistance;
		}
		else if (element is Ground ground)
		{
			R = ground.Resistance;
		}
		else if (element is Vcc vcc)
		{
			bool flag4 = direction;
			U = vcc.Potential * (flag4 ? 1.0 : (-1.0));
			R = vcc.Resistance;
		}
	}

	private void ProcessResults()
	{
		for (int i = 0; i < m_nodes.Count; i++)
		{
			Node value = m_nodes[i];
			VariableValue variable = m_equationSolver.GetVariable(i);
			value.Potential = variable.Value;
			value.IsGrounded = variable.IsConstant;
			m_nodes[i] = value;
		}
		foreach (Node node in m_nodes)
		{
			foreach (Graph<Branch>.Edge edge in m_nodeGraph.GetEdges(node.Index))
			{
				Branch value2 = edge.Value;
				if (value2.StartElement == node.ElementIndex)
				{
					Node end = ((edge.To != -1) ? m_nodes[edge.To] : Node.Empty);
					ProcessBranchResults(node, end, value2);
				}
			}
		}
	}

	private void ProcessBranchResults(Node start, Node end, Branch branch)
	{
		double num = 0.0;
		double potential = start.Potential;
		bool isGrounded = start.IsGrounded;
		int additionalVariableIndex = branch.AdditionalVariableIndex;
		switch (branch.Type)
		{
		case BranchType.Common:
			num = ((additionalVariableIndex == -1) ? ((potential - end.Potential + branch.U) * branch.InvR) : m_equationSolver.GetAdditionalVariable(additionalVariableIndex).Value);
			break;
		case BranchType.Cyclic:
			num = (branch.IsShortCircuit() ? 0.0 : (branch.U * branch.InvR));
			break;
		case BranchType.Grounded:
			num = ((additionalVariableIndex == -1) ? ((potential + branch.U) * branch.InvR) : m_equationSolver.GetAdditionalVariable(additionalVariableIndex).Value);
			break;
		case BranchType.Floating:
			num = 0.0;
			break;
		}
		CircuitElement element = start.Element;
		Electrode electrode = element.Electrodes[branch.StartElectrode];
		SimulationResult result = new SimulationResult(element, electrode, potential, 0.0 - num, isGrounded);
		ScanElement(element, electrode, direction: true, out var U, out var R);
		result.Element = element;
		result.U += U - num * R;
		UpdateElement(element, result);
		if (element.GetConnectedElectrodeCount() == 0)
		{
			return;
		}
		electrode = electrode.ConnectedElectrode;
		element = electrode.Element;
		int num2 = 0;
		while (true)
		{
			result.Element = element;
			result.Electrode = electrode;
			result.I = num;
			UpdateElement(element, result);
			ScanElement(element, electrode, direction: false, out U, out R);
			result.U += U - num * R;
			int connectedElectrodeCount = element.GetConnectedElectrodeCount();
			if (connectedElectrodeCount == 0 || connectedElectrodeCount == 1 || element.ElementIndex == branch.EndElement)
			{
				break;
			}
			electrode = (result.Electrode = element.FindNextConnectedElectrode(electrode));
			result.I = 0.0 - num;
			UpdateElement(element, result);
			electrode = electrode.ConnectedElectrode;
			element = electrode.Element;
			if (num2++ > 10000)
			{
				throw new OverflowException("Infinite loops");
			}
		}
	}

	private void UpdateElement(CircuitElement element, SimulationResult result)
	{
		m_pendingUpdates.Add(new KeyValuePair<CircuitElement, SimulationResult>(element, result));
	}

	protected void EnqueueUpdate(CircuitElement element, SimulationResult result)
	{
		m_pendingUpdates.Add(new KeyValuePair<CircuitElement, SimulationResult>(element, result));
	}

	public void FlushUpdates(CircuitSimulator simulator)
	{
		foreach (KeyValuePair<CircuitElement, SimulationResult> pair in m_pendingUpdates)
		{
			pair.Key.UpdateElectrode(this, pair.Value);
		}
		m_pendingUpdates.Clear();
	}
}

/// <summary>
/// 零分配电路求解器：与 <see cref="CircuitSimulator"/> 逐函数语义等价，但所有临时缓冲(并查集/分量分组/
/// 节点/访问标记/CSR 边表/增广矩阵/待更新列表)都跨帧复用，多线程路径使用常驻 worker 池，避免每帧 GC。
/// </summary>
public sealed class OptimizedCircuitSimulator : CircuitSimulator
{
	public int NodeCount { get; private set; }
	public int UnknownCount { get; private set; }
	private enum BranchType
	{
		Unknown = 0,
		Common = 1,
		Cyclic = 2,
		Grounded = 3,
		Floating = 4
	}

	private struct Node
	{
		public CircuitElement Element;

		public int Index;

		public double Potential;

		public bool IsGrounded;

		public bool[] Visited;

		private static readonly Node s_empty = new Node(null, -1);

		public bool IsEmpty => Index == -1;

		public int ElementIndex => Element.ElementIndex;

		public static Node Empty => s_empty;

		public Node(CircuitElement element, int index)
		{
			Element = element;
			Index = index;
			Potential = 0.0;
			IsGrounded = false;
			Visited = null;
		}
	}

	private struct Branch
	{
		public BranchType Type;

		public double U;

		public double R;

		public double InvR;

		public int StartElement;

		public int StartElectrode;

		public int EndElement;

		public int EndElectrode;

		public int AdditionalVariableIndex;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsShortCircuit()
		{
			return R == 0.0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOpenCircuit()
		{
			return double.IsPositiveInfinity(R);
		}

		public Branch(int startElement, int startElectrode, int endElement, int endElectrode)
		{
			Type = BranchType.Unknown;
			U = 0.0;
			R = 0.0;
			InvR = 0.0;
			StartElement = startElement;
			StartElectrode = startElectrode;
			EndElement = endElement;
			EndElectrode = endElectrode;
			AdditionalVariableIndex = -1;
		}
	}

	private struct VariableValue
	{
		public double Value;

		public bool IsConstant;

		public bool IsSolved;

		public VariableValue(double value, bool isConstant, bool isSolved)
		{
			Value = value;
			IsConstant = isConstant;
			IsSolved = isSolved;
		}
	}

	// 并查集（复用）
	private int[] m_ufParent;

	private int[] m_ufSize;

	// 分量分组（复用）
	private int[] m_componentIndex;

	private int[] m_componentOrder;

	private int[] m_componentStart;

	private int[] m_componentSize;

	private int[] m_componentFill;

	private List<CircuitElement>[] m_componentLists;

	// 节点（复用）
	private readonly List<Node> m_nodes = new List<Node>();

	private int[] m_nodeIndexByElement;

	private bool[][] m_nodeVisited;

	private int m_elementCount;

	// 扁平边表（复用）
	private int[] m_edgeFrom;

	private int[] m_edgeTo;

	private Branch[] m_edgeValue;

	private int m_edgeCount;

	// CSR 邻接（复用）
	private int[] m_edgeStart;

	private int[] m_csrFill;

	private int[] m_edgeSortedTo;

	private Branch[] m_edgeSortedValue;

	// 方程求解器（复用）
	private readonly OptimizedEquationSolver m_solver = new OptimizedEquationSolver();
	private readonly SparseCircuitSolver m_sparse = new SparseCircuitSolver();

	// 多线程 worker 池
	private OptimizedCircuitSimulator[] m_workers;

	public OptimizedCircuitSimulator(double deltaTime)
		: base(deltaTime)
	{
	}

	public override void Clear()
	{
		base.Clear();
		ResetScratch();
	}

	public override void Simulate(List<CircuitElement> elements)
	{
		NodeCount = 0; UnknownCount = 0;
		int count = elements.Count;
		m_elementCount = count;
		EnsureUf(count);
		for (int i = 0; i < count; i++)
		{
			elements[i].ElementIndex = i;
			m_ufParent[i] = i;
			m_ufSize[i] = 1;
		}
		for (int i = 0; i < count; i++)
		{
			CircuitElement element = elements[i];
			List<Electrode> electrodes = element.Electrodes;
			for (int e = 0; e < electrodes.Count; e++)
			{
				Electrode electrode = electrodes[e];
				if (electrode.IsConnected)
				{
					Union(m_ufParent, m_ufSize, element.ElementIndex, electrode.ConnectedElement.ElementIndex);
				}
			}
		}
		EnsureComponentBuffers(count);
		int componentCount = GetComponentIndexes(m_ufParent, m_componentIndex, count);
		if (componentCount > 0)
		{
			for (int j = 0; j < componentCount; j++)
			{
				m_componentSize[j] = 0;
			}
			for (int i = 0; i < count; i++)
			{
				int idx = m_componentIndex[i];
				if (m_componentSize[idx] == 0)
				{
					m_componentSize[idx] = m_ufSize[FindSet(m_ufParent, i)];
				}
			}
			m_componentStart[0] = 0;
			for (int j = 1; j < componentCount; j++)
			{
				m_componentStart[j] = m_componentStart[j - 1] + m_componentSize[j - 1];
			}
			for (int j = 0; j < componentCount; j++)
			{
				m_componentFill[j] = m_componentStart[j];
			}
			for (int k = 0; k < count; k++)
			{
				int idx = m_componentIndex[k];
				m_componentOrder[m_componentFill[idx]++] = k;
			}
			for (int j = 0; j < componentCount; j++)
			{
				List<CircuitElement> list = m_componentLists[j];
				list.Clear();
				int start = m_componentStart[j];
				int size = m_componentSize[j];
				for (int t = 0; t < size; t++)
				{
					CircuitElement element = elements[m_componentOrder[start + t]];
					element.CircuitIndex = j;
					list.Add(element);
				}
			}
		}
		if (ElectricalSystem.EnableMultithreadCircuit && componentCount > 1)
		{
			EnsureWorkers(componentCount);
			Parallel.For(0, componentCount, delegate(int j)
			{
				m_workers[j].SimulateCircuit(m_componentLists[j], m_elementCount);
			});
			for (int j = 0; j < componentCount; j++)
			{
				m_workers[j].FlushUpdates(this);
			}
		}
		else
		{
			for (int j = 0; j < componentCount; j++)
			{
				SimulateCircuit(m_componentLists[j], m_elementCount);
			}
			FlushUpdates(this);
		}
		for (int i = 0; i < count; i++)
		{
			elements[i].Update();
		}
	}

	private void SimulateCircuit(List<CircuitElement> elements, int elementCount)
	{
		if (elements.Count > 256)
		{
			m_sparse.Simulate(elements, this);
			NodeCount += m_sparse.NodeCount;
			UnknownCount += m_sparse.NodeCount;
			return;
		}
		ResetScratch();
		EnsureElementMap(elementCount);
		int nodeCount = 0;
		int count = elements.Count;
		for (int i = 0; i < count; i++)
		{
			CircuitElement element = elements[i];
			if (element.IsNode())
			{
				Node node = new Node(element, nodeCount);
				node.Visited = EnsureNodeVisited(nodeCount, element.Electrodes.Count);
				m_nodes.Add(node);
				m_nodeIndexByElement[element.ElementIndex] = nodeCount;
				nodeCount++;
			}
		}
		if (nodeCount == 0)
		{
			ScanSingleBranch(elements);
			return;
		}
		int additionalVariableCount = 0;
		for (int i = 0; i < nodeCount; i++)
		{
			Node node = m_nodes[i];
			List<Electrode> electrodes = node.Element.Electrodes;
			bool[] visited = node.Visited;
			for (int e = 0; e < electrodes.Count; e++)
			{
				if (electrodes[e].IsConnected && !visited[e])
				{
					ScanBranch(node, e, out var end, out var branch);
					if ((branch.Type == BranchType.Common || branch.Type == BranchType.Grounded) && branch.IsShortCircuit())
					{
						branch.AdditionalVariableIndex = additionalVariableCount;
						additionalVariableCount++;
					}
					if (end.Index == -1 || branch.Type == BranchType.Cyclic)
					{
						AddEdgeDirected(node.Index, end.Index, branch);
					}
					else
					{
						AddEdgeUndirected(node.Index, end.Index, branch);
					}
				}
			}
		}
		BuildCsr(nodeCount, m_edgeCount);
		NodeCount += nodeCount;
		UnknownCount += nodeCount + additionalVariableCount;
		m_solver.Solve(m_nodes, m_edgeStart, m_edgeSortedTo, m_edgeSortedValue, nodeCount, additionalVariableCount);
		ProcessResults();
	}

	private void ResetScratch()
	{
		if (m_nodeIndexByElement != null)
		{
			for (int i = 0; i < m_nodes.Count; i++)
			{
				int ei = m_nodes[i].Element.ElementIndex;
				if (ei >= 0 && ei < m_nodeIndexByElement.Length)
				{
					m_nodeIndexByElement[ei] = -1;
				}
			}
		}
		m_nodes.Clear();
		m_edgeCount = 0;
	}

	private void EnsureUf(int count)
	{
		if (m_ufParent == null || m_ufParent.Length < count)
		{
			int size = ((m_ufParent == null) ? 16 : (m_ufParent.Length * 2));
			while (size < count)
			{
				size *= 2;
			}
			m_ufParent = new int[size];
			m_ufSize = new int[size];
		}
	}

	private void EnsureComponentBuffers(int maxCount)
	{
		if (m_componentIndex == null || m_componentIndex.Length < maxCount)
		{
			m_componentIndex = new int[maxCount];
		}
		if (m_componentOrder == null || m_componentOrder.Length < maxCount)
		{
			m_componentOrder = new int[maxCount];
		}
		if (m_componentStart == null || m_componentStart.Length < maxCount)
		{
			m_componentStart = new int[maxCount];
		}
		if (m_componentSize == null || m_componentSize.Length < maxCount)
		{
			m_componentSize = new int[maxCount];
		}
		if (m_componentFill == null || m_componentFill.Length < maxCount)
		{
			m_componentFill = new int[maxCount];
		}
		if (m_componentLists == null || m_componentLists.Length < maxCount)
		{
			List<CircuitElement>[] old = m_componentLists;
			m_componentLists = new List<CircuitElement>[maxCount];
			if (old != null)
			{
				Array.Copy(old, m_componentLists, old.Length);
			}
			for (int i = 0; i < maxCount; i++)
			{
				if (m_componentLists[i] == null)
				{
					m_componentLists[i] = new List<CircuitElement>();
				}
			}
		}
	}

	private void EnsureElementMap(int count)
	{
		if (m_nodeIndexByElement == null || m_nodeIndexByElement.Length < count)
		{
			m_nodeIndexByElement = new int[count];
			for (int i = 0; i < count; i++)
			{
				m_nodeIndexByElement[i] = -1;
			}
		}
	}

	private bool[] EnsureNodeVisited(int index, int length)
	{
		if (m_nodeVisited == null || m_nodeVisited.Length <= index)
		{
			int size = ((m_nodeVisited == null) ? 16 : (m_nodeVisited.Length * 2));
			while (size <= index)
			{
				size *= 2;
			}
			Array.Resize(ref m_nodeVisited, size);
		}
		bool[] array = m_nodeVisited[index];
		if (array == null || array.Length != length)
		{
			array = new bool[length];
			m_nodeVisited[index] = array;
		}
		else
		{
			Array.Clear(array, 0, length);
		}
		return array;
	}

	private void EnsureWorkers(int count)
	{
		if (m_workers == null || m_workers.Length < count)
		{
			OptimizedCircuitSimulator[] old = m_workers;
			m_workers = new OptimizedCircuitSimulator[count];
			if (old != null)
			{
				Array.Copy(old, m_workers, old.Length);
			}
		}
		for (int i = 0; i < count; i++)
		{
			if (m_workers[i] == null)
			{
				m_workers[i] = new OptimizedCircuitSimulator(DeltaTime);
			}
			m_workers[i].SetDeltaTime(DeltaTime);
		}
	}

	private void GrowEdges()
	{
		int capacity = ((m_edgeFrom == null) ? 16 : (m_edgeFrom.Length * 2));
		Array.Resize(ref m_edgeFrom, capacity);
		Array.Resize(ref m_edgeTo, capacity);
		Array.Resize(ref m_edgeValue, capacity);
	}

	private void AddEdgeDirected(int from, int to, Branch value)
	{
		if (m_edgeFrom == null || m_edgeCount == m_edgeFrom.Length)
		{
			GrowEdges();
		}
		m_edgeFrom[m_edgeCount] = from;
		m_edgeTo[m_edgeCount] = to;
		m_edgeValue[m_edgeCount] = value;
		m_edgeCount++;
	}

	private void AddEdgeUndirected(int u, int v, Branch value)
	{
		AddEdgeDirected(u, v, value);
		AddEdgeDirected(v, u, value);
	}

	private void BuildCsr(int nodeCount, int edgeCount)
	{
		if (m_edgeStart == null || m_edgeStart.Length < nodeCount + 1)
		{
			m_edgeStart = new int[nodeCount + 1];
		}
		if (m_csrFill == null || m_csrFill.Length < nodeCount + 1)
		{
			m_csrFill = new int[nodeCount + 1];
		}
		if (m_edgeSortedTo == null || m_edgeSortedTo.Length < edgeCount)
		{
			m_edgeSortedTo = new int[edgeCount];
			m_edgeSortedValue = new Branch[edgeCount];
		}
		for (int i = 0; i <= nodeCount; i++)
		{
			m_edgeStart[i] = 0;
		}
		for (int e = 0; e < edgeCount; e++)
		{
			m_edgeStart[m_edgeFrom[e] + 1]++;
		}
		for (int i = 1; i <= nodeCount; i++)
		{
			m_edgeStart[i] += m_edgeStart[i - 1];
		}
		for (int i = 0; i <= nodeCount; i++)
		{
			m_csrFill[i] = m_edgeStart[i];
		}
		for (int e = 0; e < edgeCount; e++)
		{
			int u = m_edgeFrom[e];
			int pos = m_csrFill[u]++;
			m_edgeSortedTo[pos] = m_edgeTo[e];
			m_edgeSortedValue[pos] = m_edgeValue[e];
		}
	}

	private void ScanSingleBranch(List<CircuitElement> elements)
	{
		if (elements.Count <= 1)
		{
			return;
		}
		CircuitElement circuitElement = elements[0];
		foreach (CircuitElement element in elements)
		{
			if (element.GetConnectedElectrodeCount() <= 1)
			{
				circuitElement = element;
			}
		}
		Node start = new Node(circuitElement, -1);
		int startElectrode = -1;
		for (int i = 0; i < circuitElement.Electrodes.Count; i++)
		{
			if (circuitElement.Electrodes[i].IsConnected)
			{
				startElectrode = i;
				break;
			}
		}
		ScanBranch(start, startElectrode, out var _, out var branch);
		bool flag = circuitElement is Ground || circuitElement is Vcc;
		bool flag2 = branch.Type == BranchType.Grounded;
		start.IsGrounded = flag || flag2;
		if (!flag && flag2)
		{
			start.Potential = 0.0 - branch.U;
		}
		if (circuitElement.GetConnectedElectrodeCount() <= 1 && !flag)
		{
			branch.Type = BranchType.Floating;
		}
		ProcessBranchResults(start, Node.Empty, branch);
	}

	private void ScanBranch(Node start, int startElectrode, out Node end, out Branch branch)
	{
		end = Node.Empty;
		branch = new Branch(start.ElementIndex, startElectrode, -1, -1);
		CircuitElement element = start.Element;
		Electrode electrode = element.Electrodes[startElectrode];
		if (!start.IsEmpty)
		{
			start.Visited[startElectrode] = true;
		}
		ScanElement(element, electrode, direction: true, ref branch);
		if (element.GetConnectedElectrodeCount() == 0)
		{
			return;
		}
		electrode = electrode.ConnectedElectrode;
		element = electrode.Element;
		int num = 0;
		while (true)
		{
			if (element == start.Element)
			{
				branch.Type = BranchType.Cyclic;
				break;
			}
			ScanElement(element, electrode, direction: false, ref branch);
			int connectedElectrodeCount = element.GetConnectedElectrodeCount();
			if (connectedElectrodeCount == 0 || connectedElectrodeCount == 1)
			{
				if (element is Ground || element is Vcc)
				{
					branch.Type = BranchType.Grounded;
				}
				else
				{
					branch.Type = BranchType.Floating;
				}
				break;
			}
			if (connectedElectrodeCount >= 3)
			{
				branch.Type = BranchType.Common;
				break;
			}
			electrode = element.FindNextConnectedElectrode(electrode).ConnectedElectrode;
			element = electrode.Element;
			if (num++ > 10000)
			{
				throw new OverflowException("Infinite loops");
			}
		}
		branch.InvR = 1.0 / branch.R;
		if (element != null)
		{
			int electrodeIndex = element.GetElectrodeIndex(electrode);
			branch.EndElement = element.ElementIndex;
			branch.EndElectrode = electrodeIndex;
			if (element.IsNode())
			{
				end = m_nodes[m_nodeIndexByElement[element.ElementIndex]];
				end.Visited[electrodeIndex] = true;
			}
		}
	}

	private void ScanElement(CircuitElement element, Electrode electrode, bool direction, ref Branch branch)
	{
		ScanElement(element, electrode, direction, out var U, out var R);
		branch.U += U;
		branch.R += R;
	}

	private void ScanElement(CircuitElement element, Electrode electrode, bool direction, out double U, out double R)
	{
		U = 0.0;
		R = 0.0;
		if (element is Resistor resistor)
		{
			R = resistor.Resistance;
		}
		else if (element is VoltageSource voltageSource)
		{
			bool flag = (electrode == voltageSource.Cathode) ^ direction;
			U = voltageSource.Voltage * (flag ? 1.0 : (-1.0));
			R = voltageSource.Resistance;
		}
		else if (element is Capacitor capacitor)
		{
			bool flag2 = (electrode == capacitor.Cathode) ^ direction;
			U = capacitor.Charge / capacitor.Capacitance * (flag2 ? 1.0 : (-1.0));
			R = capacitor.Resistance;
		}
		else if (element is Inductor inductor)
		{
			bool flag3 = (electrode == inductor.Cathode) ^ direction;
			double num = (0.0 - inductor.MagneticFlux) / inductor.Inductance;
			U = num * inductor.Resistance * (flag3 ? 1.0 : (-1.0));
			R = inductor.Resistance;
		}
		else if (element is Ground ground)
		{
			R = ground.Resistance;
		}
		else if (element is Vcc vcc)
		{
			bool flag4 = direction;
			U = vcc.Potential * (flag4 ? 1.0 : (-1.0));
			R = vcc.Resistance;
		}
	}

	private void ProcessResults()
	{
		int count = m_nodes.Count;
		for (int i = 0; i < count; i++)
		{
			Node value = m_nodes[i];
			VariableValue variable = m_solver.GetVariable(i);
			value.Potential = variable.Value;
			value.IsGrounded = variable.IsConstant;
			m_nodes[i] = value;
		}
		for (int i = 0; i < count; i++)
		{
			Node node = m_nodes[i];
			int start = m_edgeStart[node.Index];
			int end = m_edgeStart[node.Index + 1];
			for (int x = start; x < end; x++)
			{
				Branch value2 = m_edgeSortedValue[x];
				if (value2.StartElement == node.ElementIndex)
				{
					Node to = ((m_edgeSortedTo[x] != -1) ? m_nodes[m_edgeSortedTo[x]] : Node.Empty);
					ProcessBranchResults(node, to, value2);
				}
			}
		}
	}

	private void ProcessBranchResults(Node start, Node end, Branch branch)
	{
		double num = 0.0;
		double potential = start.Potential;
		bool isGrounded = start.IsGrounded;
		int additionalVariableIndex = branch.AdditionalVariableIndex;
		switch (branch.Type)
		{
		case BranchType.Common:
			num = ((additionalVariableIndex == -1) ? ((potential - end.Potential + branch.U) * branch.InvR) : m_solver.GetAdditionalVariable(additionalVariableIndex).Value);
			break;
		case BranchType.Cyclic:
			num = (branch.IsShortCircuit() ? 0.0 : (branch.U * branch.InvR));
			break;
		case BranchType.Grounded:
			num = ((additionalVariableIndex == -1) ? ((potential + branch.U) * branch.InvR) : m_solver.GetAdditionalVariable(additionalVariableIndex).Value);
			break;
		case BranchType.Floating:
			num = 0.0;
			break;
		}
		CircuitElement element = start.Element;
		Electrode electrode = element.Electrodes[branch.StartElectrode];
		SimulationResult result = new SimulationResult(element, electrode, potential, 0.0 - num, isGrounded);
		ScanElement(element, electrode, direction: true, out var U, out var R);
		result.Element = element;
		result.U += U - num * R;
		EnqueueUpdate(element, result);
		if (element.GetConnectedElectrodeCount() == 0)
		{
			return;
		}
		electrode = electrode.ConnectedElectrode;
		element = electrode.Element;
		int num2 = 0;
		while (true)
		{
			result.Element = element;
			result.Electrode = electrode;
			result.I = num;
			EnqueueUpdate(element, result);
			ScanElement(element, electrode, direction: false, out U, out R);
			result.U += U - num * R;
			int connectedElectrodeCount = element.GetConnectedElectrodeCount();
			if (connectedElectrodeCount == 0 || connectedElectrodeCount == 1 || element.ElementIndex == branch.EndElement)
			{
				break;
			}
			electrode = (result.Electrode = element.FindNextConnectedElectrode(electrode));
			result.I = 0.0 - num;
			EnqueueUpdate(element, result);
			electrode = electrode.ConnectedElectrode;
			element = electrode.Element;
			if (num2++ > 10000)
			{
				throw new OverflowException("Infinite loops");
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindSet(int[] parent, int x)
	{
		int num = parent[x];
		if (x != num)
		{
			num = FindSet(parent, num);
			parent[x] = num;
		}
		return num;
	}

	private static void Union(int[] parent, int[] size, int x, int y)
	{
		int num = FindSet(parent, x);
		int num2 = FindSet(parent, y);
		if (num == num2)
		{
			return;
		}
		if (size[num] > size[num2])
		{
			size[num] += size[num2];
			parent[num2] = num;
		}
		else
		{
			size[num2] += size[num];
			parent[num] = num2;
		}
	}

	private static int GetComponentIndexes(int[] parent, int[] componentIndexes, int count)
	{
		for (int i = 0; i < count; i++)
		{
			componentIndexes[i] = -1;
		}
		int componentCount = 0;
		for (int j = 0; j < count; j++)
		{
			int num = FindSet(parent, j);
			int num2 = componentIndexes[num];
			if (num2 == -1)
			{
				num2 = (componentIndexes[num] = componentCount++);
			}
			componentIndexes[j] = num2;
		}
		return componentCount;
	}

	private sealed class OptimizedEquationSolver
	{
		private int m_equationCount;

		private int m_variableCount;

		private int m_additionalVariableCount;

		private int[] m_ufParent;

		private int[] m_ufSize;

		private int[] m_variableIndexes;

		private double[][] m_augmentedMatrix;

		private VariableValue[] m_variables;

		public OptimizedEquationSolver()
		{
			m_variableIndexes = Array.Empty<int>();
			m_augmentedMatrix = Array.Empty<double[]>();
			m_variables = Array.Empty<VariableValue>();
		}

		public VariableValue GetVariable(int index)
		{
			int num = m_variableIndexes[index];
			if (num == -1)
			{
				return new VariableValue(0.0, isConstant: true, isSolved: true);
			}
			return m_variables[num];
		}

		public VariableValue GetAdditionalVariable(int index)
		{
			return m_variables[m_variableCount + index];
		}

		public void Solve(List<Node> nodes, int[] edgeStart, int[] edgeTo, Branch[] edgeValue, int nodeCount, int additionalVariableCount)
		{
			Initialize(edgeStart, edgeTo, edgeValue, nodeCount, additionalVariableCount);
			CreateEquations(nodes, edgeStart, edgeTo, edgeValue, nodeCount);
			SolveEquations();
		}

		private void Initialize(int[] edgeStart, int[] edgeTo, Branch[] edgeValue, int count, int additionalVariableCount)
		{
			EnsureUf(count + 1);
			for (int i = 0; i <= count; i++)
			{
				m_ufParent[i] = i;
				m_ufSize[i] = 1;
			}
			for (int ni = 0; ni < count; ni++)
			{
				int start = edgeStart[ni];
				int end = edgeStart[ni + 1];
				for (int x = start; x < end; x++)
				{
					Branch value = edgeValue[x];
					if (!value.IsShortCircuit())
					{
						continue;
					}
					switch (value.Type)
					{
					case BranchType.Common:
						Union(m_ufParent, m_ufSize, ni, edgeTo[x]);
						break;
					case BranchType.Grounded:
						Union(m_ufParent, m_ufSize, ni, count);
						break;
					}
				}
			}
			if (m_variableIndexes.Length < count + 1)
			{
				m_variableIndexes = new int[count + 1];
			}
			int num = GetComponentIndexes(m_ufParent, m_variableIndexes, count + 1);
			int num2 = m_variableIndexes[count];
			for (int i = 0; i < count; i++)
			{
				int num3 = m_variableIndexes[i];
				if (num3 > num2)
				{
					m_variableIndexes[i] = num3 - 1;
				}
				else if (num3 == num2)
				{
					m_variableIndexes[i] = -1;
				}
			}
			m_equationCount = count;
			m_variableCount = num - 1;
			m_additionalVariableCount = additionalVariableCount;
		}

		private void EnsureUf(int count)
		{
			if (m_ufParent == null || m_ufParent.Length < count)
			{
				int size = ((m_ufParent == null) ? 16 : (m_ufParent.Length * 2));
				while (size < count)
				{
					size *= 2;
				}
				m_ufParent = new int[size];
				m_ufSize = new int[size];
			}
		}

		private void CreateEquations(List<Node> nodes, int[] edgeStart, int[] edgeTo, Branch[] edgeValue, int nodeCount)
		{
			double[][] array = m_augmentedMatrix;
			int equationCount = m_equationCount;
			int num = m_variableCount + m_additionalVariableCount;
			if (equationCount > array.Length || (array.Length != 0 && num + 1 > array[0].Length))
			{
				array = new double[equationCount][];
				for (int i = 0; i < equationCount; i++)
				{
					array[i] = new double[num + 1];
				}
				m_augmentedMatrix = array;
			}
			else
			{
				for (int i = 0; i < equationCount; i++)
				{
					Array.Clear(array[i], 0, num + 1);
				}
			}
			for (int ni = 0; ni < nodeCount; ni++)
			{
				double[] array2 = array[ni];
				int start = edgeStart[ni];
				int end = edgeStart[ni + 1];
				for (int x = start; x < end; x++)
				{
					Branch value = edgeValue[x];
					bool num2 = value.StartElement == nodes[ni].ElementIndex;
					int num3 = value.AdditionalVariableIndex;
					if (num3 != -1)
					{
						num3 += m_variableCount;
					}
					int num4 = m_variableIndexes[ni];
					int num5 = ((edgeTo[x] != -1) ? m_variableIndexes[edgeTo[x]] : (-1));
					double num6 = 1.0;
					double num7 = value.InvR;
					if (!num2)
					{
						int num8 = num4;
						num4 = num5;
						num5 = num8;
						num6 = 0.0 - num6;
						num7 = 0.0 - num7;
					}
					BranchType type = value.Type;
					if (type != BranchType.Common && type != BranchType.Grounded)
					{
						continue;
					}
					if (num3 != -1)
					{
						array2[num3] += num6;
						continue;
					}
					if (num4 != -1)
					{
						array2[num4] += num7;
					}
					if (num5 != -1)
					{
						array2[num5] -= num7;
					}
					array2[num] -= value.U * num7;
				}
			}
		}

		private void SolveEquations()
		{
			double[][] augmentedMatrix = m_augmentedMatrix;
			int equationCount = m_equationCount;
			int num = m_variableCount + m_additionalVariableCount;
			if (num > m_variables.Length)
			{
				m_variables = new VariableValue[num];
			}
			else
			{
				Array.Clear(m_variables, 0, num);
			}
			int num2 = 0;
			int i = 0;
			while (num2 < equationCount)
			{
				int num3 = num2;
				double num4 = augmentedMatrix[num2][i];
				num4 = ((num4 >= 0.0) ? num4 : (0.0 - num4));
				for (; i < num; i++)
				{
					for (int j = num2 + 1; j < equationCount; j++)
					{
						double num5 = augmentedMatrix[j][i];
						num5 = ((num5 >= 0.0) ? num5 : (0.0 - num5));
						if (num5 > num4)
						{
							num3 = j;
							num4 = num5;
						}
					}
					if (num4 > 1E-08)
					{
						break;
					}
				}
				if (i == num)
				{
					break;
				}
				if (num3 != num2)
				{
					double[] array = augmentedMatrix[num2];
					augmentedMatrix[num2] = augmentedMatrix[num3];
					augmentedMatrix[num3] = array;
				}
				double num6 = augmentedMatrix[num2][i];
				for (int k = num2 + 1; k < equationCount; k++)
				{
					double num7 = augmentedMatrix[k][i] / num6;
					augmentedMatrix[k][i] = 0.0;
					for (int l = i + 1; l < num + 1; l++)
					{
						augmentedMatrix[k][l] -= augmentedMatrix[num2][l] * num7;
					}
				}
				num2++;
				i++;
			}
			for (int num8 = equationCount - 1; num8 >= 0; num8--)
			{
				int num9 = -1;
				double num10 = 0.0;
				bool flag = true;
				double num11 = augmentedMatrix[num8][num];
				for (int m = num8; m < num; m++)
				{
					double num12 = augmentedMatrix[num8][m];
					if (!(-1E-08 <= num12) || !(num12 <= 1E-08))
					{
						ref VariableValue reference = ref m_variables[m];
						if (reference.IsSolved)
						{
							num11 -= num12 * reference.Value;
							flag &= reference.IsConstant;
						}
						else if (num9 != -1)
						{
							reference.IsConstant = m >= m_variableCount;
							reference.IsSolved = true;
							flag &= reference.IsConstant;
						}
						else
						{
							num9 = m;
							num10 = num12;
						}
					}
				}
				if (num9 != -1)
				{
					m_variables[num9] = new VariableValue(num11 / num10, flag, isSolved: true);
				}
			}
			for (int n = 0; n < num; n++)
			{
				if (!m_variables[n].IsSolved)
				{
					m_variables[n] = new VariableValue(0.0, isConstant: false, isSolved: true);
				}
			}
		}
	}
}

public abstract class CircuitElement
{
	protected List<Electrode> m_electrodes;

	public int ElementIndex { get; set; }

	public int CircuitIndex { get; set; }

	public virtual int DefaultElectrodeCount { get; }

	public List<Electrode> Electrodes => m_electrodes;

	public IEnumerable<Electrode> ConnectedElectrodes
	{
		get
		{
			foreach (Electrode electrode in m_electrodes)
			{
				if (electrode.IsConnected)
				{
					yield return electrode;
				}
			}
		}
	}

	public event Action<CircuitSimulator, SimulationResult> ElementUpdated;

	protected CircuitElement()
	{
		CreateElectrodes(DefaultElectrodeCount);
	}

	protected void CreateElectrodes(int count)
	{
		m_electrodes = new List<Electrode>(count);
		for (int i = 0; i < count; i++)
		{
			m_electrodes.Add(new Electrode(this, null));
		}
	}

	public int GetConnectedElectrodeCount()
	{
		int num = 0;
		foreach (Electrode electrode in m_electrodes)
		{
			if (electrode.IsConnected)
			{
				num++;
			}
		}
		return num;
	}

	public int GetElectrodeIndex(Electrode electrode)
	{
		return m_electrodes.IndexOf(electrode);
	}

	public Electrode FindElectrode(CircuitElement connectedElement)
	{
		for (int i = 0; i < m_electrodes.Count; i++)
			if (m_electrodes[i].ConnectedElement == connectedElement) return m_electrodes[i];
		return null;
	}

	public Electrode FindNextElectrode(Electrode electrode)
	{
		for (int i = 0; i < m_electrodes.Count; i++)
			if (m_electrodes[i] != electrode) return m_electrodes[i];
		return null;
	}

	public Electrode FindNextConnectedElectrode(Electrode electrode)
	{
		for (int i = 0; i < m_electrodes.Count; i++)
			if (m_electrodes[i].IsConnected && m_electrodes[i] != electrode) return m_electrodes[i];
		return null;
	}

	public virtual bool IsNode()
	{
		return false;
	}

	public virtual void Update()
	{
	}

	public virtual void UpdateElectrode(CircuitSimulator simulator, SimulationResult result)
	{
		this.ElementUpdated?.Invoke(simulator, result);
	}
}

public class Electrode
{
	private CircuitElement m_element;

	private Electrode m_connectedElectrode;

	private bool m_closed;

	// 标记：本电极是本模组“斜向发送器”新增的无线链路电极，计算等电位分组时应忽略（避免自反馈）
	public bool IsWirelessLink;

	public CircuitElement Element => m_element;

	public CircuitElement ConnectedElement => m_connectedElectrode?.m_element;

	public Electrode ConnectedElectrode => m_connectedElectrode;

	public bool IsClosed => m_closed;

	public bool IsConnected
	{
		get
		{
			if (m_closed && m_connectedElectrode != null)
			{
				return m_connectedElectrode.m_closed;
			}
			return false;
		}
	}

	public Electrode(CircuitElement element, Electrode connectedElectrode)
		: this(element, connectedElectrode, closed: true)
	{
	}

	public Electrode(CircuitElement element, Electrode connectedElectrode, bool closed)
	{
		m_element = element;
		m_connectedElectrode = connectedElectrode;
		m_closed = closed;
	}

	public void Connect(Electrode connectedElectrode)
	{
		m_connectedElectrode = connectedElectrode;
	}

	public void Disconnect()
	{
		m_connectedElectrode = null;
	}

	public void Switch(bool closed)
	{
		m_closed = closed;
	}
}

public static class CircuitFactory
{
	public static void Connect(Electrode left, Electrode right)
	{
		left.Connect(right);
		right.Connect(left);
	}

	public static void Disconnect(Electrode left, Electrode right)
	{
		left.Disconnect();
		right.Disconnect();
	}

	public static bool IsConnected(Electrode left, Electrode right)
	{
		if (left.IsConnected)
		{
			return right.IsConnected;
		}
		return false;
	}

	public static bool IsConnected(CircuitElement left, CircuitElement right)
	{
		return left.FindElectrode(right) != null;
	}
}

public struct SimulationResult
{
	public CircuitElement Element;

	public Electrode Electrode;

	public double U;

	public double I;

	public bool IsGrounded;

	public SimulationResult(CircuitElement element, Electrode electrode, double u, double i, bool isGrounded)
	{
		Element = element;
		Electrode = electrode;
		U = u;
		I = i;
		IsGrounded = isGrounded;
	}
}

public class DisjointSet
{
	private int m_count;

	private int[] m_parent;

	private int[] m_size;

	public int Count => m_count;

	public DisjointSet(int count)
	{
		m_count = count;
		if (count == 0)
		{
			m_parent = Array.Empty<int>();
			m_size = Array.Empty<int>();
			return;
		}
		m_parent = new int[count];
		m_size = new int[count];
		for (int i = 0; i < count; i++)
		{
			MakeSet(i);
		}
	}

	public void MakeSet(int x)
	{
		m_parent[x] = x;
		m_size[x] = 1;
	}

	public int FindSet(int x)
	{
		int num = m_parent[x];
		if (x != num)
		{
			return m_parent[x] = FindSet(num);
		}
		return num;
	}

	public int FindSet(int x, out int size)
	{
		int num = FindSet(x);
		size = m_size[num];
		return num;
	}

	public void Union(int x, int y)
	{
		int num = FindSet(x);
		int num2 = FindSet(y);
		if (num != num2)
		{
			if (m_size[num] > m_size[num2])
			{
				m_size[num] += m_size[num2];
				m_parent[num2] = num;
			}
			else
			{
				m_size[num2] += m_size[num];
				m_parent[num] = num2;
			}
		}
	}

	public void Clear()
	{
		for (int i = 0; i < m_count; i++)
		{
			MakeSet(i);
		}
	}
}

public static class DisjointSetExtensions
{
	public static int[] ToArray(this DisjointSet disjointSet)
	{
		int count = disjointSet.Count;
		int[] array = new int[count];
		for (int i = 0; i < count; i++)
		{
			array[i] = disjointSet.FindSet(i);
		}
		return array;
	}

	public static int[] GetComponentIndexes(this DisjointSet disjointSet, out int componentCount)
	{
		int[] array = new int[disjointSet.Count];
		disjointSet.GetComponentIndexes(array, out componentCount);
		return array;
	}

	public static void GetComponentIndexes(this DisjointSet disjointSet, int[] componentIndexes, out int componentCount)
	{
		int count = disjointSet.Count;
		if (componentIndexes.Length < count)
		{
			throw new ArgumentException("componentIndexes");
		}
		componentCount = 0;
		for (int i = 0; i < count; i++)
		{
			componentIndexes[i] = -1;
		}
		for (int j = 0; j < count; j++)
		{
			int num = disjointSet.FindSet(j);
			int num2 = componentIndexes[num];
			if (num2 == -1)
			{
				num2 = (componentIndexes[num] = componentCount++);
			}
			componentIndexes[j] = num2;
		}
	}

	public static int[] GetComponents(this DisjointSet disjointSet, out int[] size, out int componentCount)
	{
		int count = disjointSet.Count;
		int[] componentIndexes = disjointSet.GetComponentIndexes(out componentCount);
		int[] array = new int[componentCount];
		int[] array2 = new int[count];
		size = new int[componentCount];
		for (int i = 0; i < count; i++)
		{
			int num = componentIndexes[i];
			if (size[num] == 0)
			{
				disjointSet.FindSet(i, out size[num]);
			}
		}
		for (int j = 1; j < componentCount; j++)
		{
			array[j] = array[j - 1] + size[j - 1];
		}
		for (int k = 0; k < count; k++)
		{
			int num2 = componentIndexes[k];
			array2[array[num2]] = k;
			array[num2]++;
		}
		return array2;
	}
}

public class Graph<T>
{
	public struct Edge
	{
		public int To;

		public T Value;

		public Edge(int to, T value)
		{
			To = to;
			Value = value;
		}
	}

	private int m_count;

	private List<Edge>[] m_graph;

	public int Count => m_count;

	public Graph(int count)
	{
		m_count = count;
		m_graph = new List<Edge>[count];
		for (int i = 0; i < count; i++)
		{
			m_graph[i] = new List<Edge>();
		}
	}

	public void AddDirectedEdge(int u, int v, T value)
	{
		m_graph[u].Add(new Edge(v, value));
	}

	public void AddUndirectedEdge(int u, int v, T value)
	{
		AddDirectedEdge(u, v, value);
		AddDirectedEdge(v, u, value);
	}

	public bool Contains(int u, int v)
	{
		foreach (Edge item in m_graph[u])
		{
			if (item.To == v)
			{
				return true;
			}
		}
		return false;
	}

	public List<Edge> GetEdges(int u)
	{
		return m_graph[u];
	}

	public void Resize(int newCount)
	{
		Array.Resize(ref m_graph, newCount);
		for (int i = m_count; i < newCount; i++)
		{
			m_graph[i] = new List<Edge>();
		}
		m_count = newCount;
	}

	public void Clear()
	{
		for (int i = 0; i < m_count; i++)
		{
			m_graph[i].Clear();
		}
	}
}

public class Resistor : CircuitElement
{
	public double Resistance { get; set; }

	public override int DefaultElectrodeCount => 2;

	public Electrode Electrode1 => m_electrodes[0];

	public Electrode Electrode2 => m_electrodes[1];

	public Resistor(double resistance)
	{
		Resistance = resistance;
	}
}

public class VoltageSource : CircuitElement
{
	public const int AnodeIndex = 0;

	public const int CathodeIndex = 1;

	public double Voltage { get; set; }

	public double Resistance { get; set; }

	public override int DefaultElectrodeCount => 2;

	public Electrode Anode => m_electrodes[0];

	public Electrode Cathode => m_electrodes[1];

	public VoltageSource(double voltage, double resistance)
	{
		Voltage = voltage;
		Resistance = resistance;
	}
}

public class Capacitor : CircuitElement
{
	public const int AnodeIndex = 0;

	public const int CathodeIndex = 1;

	private double m_I;

	private double m_deltaTime;

	public double Capacitance { get; set; }

	public double Charge { get; set; }

	public double Resistance { get; set; }

	public override int DefaultElectrodeCount => 2;

	public Electrode Anode => m_electrodes[0];

	public Electrode Cathode => m_electrodes[1];

	public Capacitor(double capacitance, double resistance)
		: this(capacitance, 0.0, resistance)
	{
	}

	public Capacitor(double capacitance, double charge, double resistance)
	{
		Capacitance = capacitance;
		Charge = charge;
		Resistance = resistance;
		m_I = double.NaN;
	}

	public override void UpdateElectrode(CircuitSimulator simulator, SimulationResult result)
	{
		base.UpdateElectrode(simulator, result);
		bool flag = result.Electrode == Anode;
		m_I = (flag ? result.I : (0.0 - result.I));
		m_deltaTime = simulator.DeltaTime;
	}

	public override void Update()
	{
		base.Update();
		if (!double.IsNaN(m_I))
		{
			Charge += m_I * m_deltaTime;
		}
		m_I = double.NaN;
	}
}

public class Inductor : CircuitElement
{
	public const int AnodeIndex = 0;

	public const int CathodeIndex = 1;

	private double m_U1;

	private double m_U2;

	private double m_deltaTime;

	private double m_I;

	private readonly List<Inductor> m_coupled = new List<Inductor>();

	private readonly List<double> m_coupledM = new List<double>();

	public double Inductance { get; set; }

	public double MagneticFlux { get; set; }

	// 互感耦合进来的磁链(Σ M * 对方电流)。与自身 MagneticFlux 相加得到 TotalFlux，
	// 于是"被铁框链条包裹的多个电感"会互相感应(变压器)。
	public double MutualFlux { get; private set; }

	public double TotalFlux => MagneticFlux + MutualFlux;

	// 最近一次测得的(带符号)电流，供互感伙伴读取。
	public double Current => m_I;

	public int CouplingCount => m_coupled.Count;

	public double Resistance { get; set; }

	public override int DefaultElectrodeCount => 2;

	public Electrode Anode => m_electrodes[0];

	public Electrode Cathode => m_electrodes[1];

	public Inductor(double inductance, double resistance)
		: this(inductance, 0.0, resistance)
	{
	}

	public Inductor(double inductance, double magneticFlux, double resistance)
	{
		Inductance = inductance;
		MagneticFlux = magneticFlux;
		Resistance = resistance;
		m_U1 = double.NaN;
		m_U2 = double.NaN;
	}

	public void ClearCouplings()
	{
		m_coupled.Clear();
		m_coupledM.Clear();
		MutualFlux = 0.0;
	}

	public void AddCoupling(Inductor partner, double mutual)
	{
		if (partner != null && partner != this && !double.IsNaN(mutual) && mutual != 0.0)
		{
			m_coupled.Add(partner);
			m_coupledM.Add(mutual);
		}
	}

	public override void UpdateElectrode(CircuitSimulator simulator, SimulationResult result)
	{
		base.UpdateElectrode(simulator, result);
		bool flag = result.Electrode == Anode;
		if (flag)
		{
			m_U1 = result.U;
		}
		else
		{
			m_U2 = result.U;
		}
		m_I = (flag ? result.I : (0.0 - result.I));
		m_deltaTime = simulator.DeltaTime;
	}

	public override void Update()
	{
		base.Update();
		if (!double.IsNaN(m_U1) && !double.IsNaN(m_U2))
		{
			MagneticFlux += (m_U1 - m_U2) * m_deltaTime;
		}
		double mutual = 0.0;
		for (int i = 0; i < m_coupled.Count; i++)
		{
			Inductor partner = m_coupled[i];
			if (partner != null)
			{
				mutual += m_coupledM[i] * partner.Current;
			}
		}
		MutualFlux = mutual;
		m_U1 = double.NaN;
		m_U2 = double.NaN;
	}
}

public class Ground : CircuitElement
{
	public double Resistance { get; set; }

	public override int DefaultElectrodeCount => 1;

	public Electrode Electrode => m_electrodes[0];

	public Ground()
		: this(0.0)
	{
	}

	public Ground(double resistance)
	{
		Resistance = resistance;
	}
}

public class Vcc : CircuitElement
{
	public double Potential { get; set; }

	public double Resistance { get; set; }

	public override int DefaultElectrodeCount => 1;

	public Electrode Electrode => m_electrodes[0];

	public Vcc(double potential)
		: this(potential, 0.0)
	{
	}

	public Vcc(double potential, double resistance)
	{
		Potential = potential;
		Resistance = resistance;
	}
}

public class Wire : CircuitElement
{
	public override int DefaultElectrodeCount => 0;

	public Wire()
		: this(0)
	{
	}

	public Wire(int electrodeCount)
	{
		if (electrodeCount != 0)
		{
			CreateElectrodes(electrodeCount);
		}
	}

	public override bool IsNode()
	{
		return GetConnectedElectrodeCount() >= 3;
	}
}

public class Switch : CircuitElement
{
	private bool m_closed;

	public const int PoleIndex = 0;

	public const int ThrowIndex = 1;

	public bool IsClosed => m_closed;

	public override int DefaultElectrodeCount => 2;

	public Electrode Pole => m_electrodes[0];

	public Electrode Throw => m_electrodes[1];

	public Switch()
	{
		m_closed = false;
		ToggleInternal(m_closed);
	}

	public void Toggle(bool closed)
	{
		if (m_closed != closed)
		{
			m_closed = closed;
			ToggleInternal(closed);
		}
	}

	private void ToggleInternal(bool closed)
	{
		Throw.Switch(closed);
	}
}

public class SPDTSwitch : CircuitElement
{
	private bool m_closed;

	public const int PoleIndex = 0;

	public const int Throw1Index = 1;

	public const int Throw2Index = 2;

	public bool IsClosed => m_closed;

	public override int DefaultElectrodeCount => 3;

	public Electrode Pole => m_electrodes[0];

	public Electrode Throw1 => m_electrodes[1];

	public Electrode Throw2 => m_electrodes[2];

	public SPDTSwitch()
	{
		m_closed = false;
		ToggleInternal(m_closed);
	}

	public void Toggle(bool closed)
	{
		if (m_closed != closed)
		{
			m_closed = closed;
			ToggleInternal(closed);
		}
	}

	private void ToggleInternal(bool closed)
	{
		Throw1.Switch(!closed);
		Throw2.Switch(closed);
	}
}

}
