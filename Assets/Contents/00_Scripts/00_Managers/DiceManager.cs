using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DiceManager : MonoBehaviour
{
    [Header("프리팹 및 슬롯 설정")]
    public GameObject dicePrefab;
    public Transform[] keepSlots;   // 주사위를 보관(Keep)할 장소들
    public Transform[] rollSlots;   // 주사위를 굴릴(Roll) 장소들

    [Header("다른 스크립트 참조")]
    private UIManager ui;
    private List<Dice> activeDiceList = new List<Dice>(); // 현재 생성된 주사위들의 리스트
    private Dice[] keepSlotOccupants; // 보관함 각 칸에 어떤 주사위가 들어있는지 체크용 배열

    [Header("게임 진행 데이터")]
    public int targetScore = 100;
    public int currentStage = 1;

    [Header("룰 설정")]
    public int maxPlays = 3;         // 한 스테이지에서 점수를 제출할 수 있는 최대 횟수
    private int currentPlayNum;      // 현재 몇 번째 제출 기회인지
    private int accumulatedScore;   // 현재 스테이지에서 쌓은 총 점수

    public int maxRerolls = 2;       // 한 라운드에서 다시 굴릴 수 있는 최대 횟수
    private int currentRerolls;      // 현재 다시 굴리기를 수행한 횟수

    // 오브젝트가 생성될 때 UI 참조를 가져오고 버튼 이벤트를 연결합니다.
    void Awake()
    {
        ui = FindObjectOfType<UIManager>();
        keepSlotOccupants = new Dice[keepSlots.Length];

        // 주사위의 Keep 상태가 변할 때마다 HandleDiceChanged 함수를 실행하도록 연결합니다.
        Dice.OnDiceStateChanged += HandleDiceChanged;

        // UI 버튼들에 실제 기능을 연결합니다. (상점 가기, 다음 스테이지 가기)
        if (ui.goShopButton != null) ui.goShopButton.onClick.AddListener(GoToShop);
        if (ui.nextStageButton != null) ui.nextStageButton.onClick.AddListener(SkipShopAndNextStage);
    }

    // 게임이 시작되면 첫 번째 스테이지를 세팅합니다.
    void Start() => StartNewStage();

    //  주사위를 클릭해 Keep 하거나 뺄 때마다 호출되어 실시간 점수와 UI를 갱신합니다.
    void HandleDiceChanged()
    {
        int keptCount = 0;
        bool hasDiceToRoll = false;

        // 모든 주사위를 돌며 보관함에 넣을지, 굴리기 영역으로 뺄지 결정합니다.
        foreach (var d in activeDiceList)
        {
            if (d == null) continue;

            if (d.isKept)
            {
                // 보관함에 안 들어있는데 Keep 상태라면 빈 보관 슬롯에 할당합니다.
                if (d.currentKeepIndex == -1) AssignToKeepSlot(d);
                keptCount++;
            }
            else
            {
                // 보관함에 있는데 Keep 상태가 아니라면 보관함에서 해제합니다.
                if (d.currentKeepIndex != -1) ReleaseFromKeepSlot(d);
                hasDiceToRoll = true;
            }
        }

        // 현재 필드에 있는 주사위들의 눈금 값을 리스트로 추출합니다.
        List<int> allValues = activeDiceList.Where(d => d != null).Select(d => d.currentValue).ToList();

        float multiplier = 1.0f;
        string handName = "에러";
        int totalBoardSum = 0;

        // 보관함에 주사위가 꽉 찼을 때만 족보(Hand)를 계산합니다.
        if (allValues.Count == keepSlots.Length)
        {
            totalBoardSum = allValues.Sum();
            CalculateHandData(allValues, out multiplier, out handName);
        }

        // 갱신된 정보를 바탕으로 화면 UI를 업데이트합니다.
        ui.UpdateGameUI(currentStage, accumulatedScore, targetScore, currentPlayNum, maxPlays, maxRerolls - currentRerolls, handName, totalBoardSum, multiplier);

        // 다시 굴리기 버튼과 제출 버튼의 활성화 상태를 제어합니다.
        bool canRoll = (currentRerolls < maxRerolls) && hasDiceToRoll;
        ui.SetRollButtonInteractable(canRoll);
        ui.SetFinishButtonInteractable(keptCount == keepSlots.Length);
    }

    //  주사위 숫자 조합을 분석해 어떤 족보(원페어, 스트레이트 등)인지 판별합니다.
    void CalculateHandData(List<int> values, out float multiplier, out string handName)
    {
        multiplier = 1.0f; handName = "탑 (High Card)";
        int[] counts = new int[7]; foreach (int v in values) counts[v]++;
        List<int> sortedValues = new List<int>(values); sortedValues.Sort();

        // 족보 판단 로직 (파이브카드, 스트레이트, 포카드 등 우선순위대로 체크)
        if (counts.Any(c => c == 5)) { multiplier = 2.5f; handName = "파이브 카드"; return; }
        bool isStraight = true;
        for (int i = 0; i < sortedValues.Count - 1; i++)
            if (sortedValues[i] + 1 != sortedValues[i + 1]) { isStraight = false; break; }
        if (isStraight) { multiplier = 2.0f; handName = "스트레이트"; return; }
        if (counts.Any(c => c == 4)) { multiplier = 1.8f; handName = "포카드"; return; }
        if (counts.Any(c => c == 3) && counts.Any(c => c == 2)) { multiplier = 1.7f; handName = "풀하우스"; return; }
        if (counts.Any(c => c == 3)) { multiplier = 1.5f; handName = "트리플"; return; }
        if (counts.Count(c => c == 2) == 2) { multiplier = 1.4f; handName = "투 페어"; return; }
        if (counts.Any(c => c == 2)) { multiplier = 1.2f; handName = "원 페어"; return; }
    }

    // 'Roll' 버튼 클릭 시, 선택되지 않은 주사위들을 새로 굴립니다.
    public void OnRollButtonClick()
    {
        if (currentRerolls >= maxRerolls) return;
        bool hasDiceToRoll = false;
        foreach (var d in activeDiceList) if (!d.isKept) { hasDiceToRoll = true; break; }
        if (!hasDiceToRoll) return;

        // 선택되지 않은 주사위들의 눈금을 1~6 사이의 랜덤 값으로 새로 설정합니다.
        foreach (var d in activeDiceList) if (!d.isKept) d.SetValue(UnityEngine.Random.Range(1, 7));
        currentRerolls++;
        HandleDiceChanged(); // 주사위 값이 바뀌었으므로 UI를 다시 갱신합니다.
    }

    // Finish' 버튼 클릭 시, 이번 제출 점수를 확정하고 스테이지 클리어 여부를 확인합니다.
    public void OnFinishButtonClick()
    {
        int baseSum = 0;
        List<int> keptValues = new List<int>();
        foreach (var d in activeDiceList)
        {
            if (d != null && d.isKept)
            {
                baseSum += d.currentValue;
                keptValues.Add(d.currentValue);
            }
        }

        // 최종 점수를 계산하여 누적 점수에 합산합니다.
        float multiplier; string handName;
        CalculateHandData(keptValues, out multiplier, out handName);
        int finalScoreForTurn = Mathf.FloorToInt(baseSum * multiplier);

        accumulatedScore += finalScoreForTurn;

        // 제출 후 잠시 버튼을 못 누르게 막습니다.
        ui.SetRollButtonInteractable(false);
        ui.SetFinishButtonInteractable(false);

        // 목표 점수 달성 여부에 따라 결과를 출력합니다.
        if (accumulatedScore >= targetScore)
        {
            ui.ShowResult("스테이지 클리어!", "#00FF00", $"최종 누적 점수: {accumulatedScore}\n목표를 달성했습니다!");
            Invoke("PromptShopChoice", 1.5f);
        }
        else if (currentPlayNum >= maxPlays)
        {
            ui.ShowResult("스테이지 실패...", "#FF0000", $"최종 누적 점수: {accumulatedScore} / {targetScore}\n다시 도전하세요.");
            Invoke("RestartGame", 1.5f);
        }
        else
        {
            currentPlayNum++;
            Invoke("StartNewRound", 0.5f); // 아직 기회가 남았다면 다음 라운드를 시작합니다.
        }
    }

    // 결과창을 닫고 상점에 갈지 선택하는 창을 보여줍니다.
    void PromptShopChoice()
    {
        ui.HideResult();
        ui.ShowShopChoice();
    }

    //상점 이동 버튼을 눌렀을 때 실행되는 함수입니다.
    public void GoToShop()
    {
        ui.HideShopChoice();
        Debug.Log("상점 시스템으로 이동!");
    }

    //상점을 들르지 않고 바로 다음 스테이지로 넘어갑니다.
    public void SkipShopAndNextStage()
    {
        ui.HideShopChoice();
        NextStage();
    }

    //스테이지 번호와 난이도(목표 점수)를 높이고 게임을 시작합니다.
    void NextStage()
    {
        currentStage++;
        targetScore += 30;
        StartNewStage();
    }

    //게임 오버 시 1스테이지부터 초기 상태로 다시 시작합니다.
    void RestartGame()
    {
        currentStage = 1;
        targetScore = 100;
        StartNewStage();
    }

    //새로운 스테이지를 위해 점수와 플레이 횟수를 리셋합니다.
    void StartNewStage()
    {
        accumulatedScore = 0;
        currentPlayNum = 1;
        StartNewRound();
    }

    //제출 기회 1회를 위해 주사위를 새로 깔고 리롤 횟수를 리셋합니다.
    void StartNewRound()
    {
        ui.HideResult();
        currentRerolls = 0;
        SpawnDice();
        HandleDiceChanged();
    }

    // 선택한 주사위를 보관함의 빈칸에 물리적으로 배치하고 애니메이션 이동시킵니다.
    void AssignToKeepSlot(Dice d)
    {
        for (int i = 0; i < keepSlotOccupants.Length; i++)
        {
            if (keepSlotOccupants[i] == null)
            {
                keepSlotOccupants[i] = d; d.currentKeepIndex = i;
                d.MoveToTarget(keepSlots[i].position); break;
            }
        }
    }

    //보관함에서 뺀 주사위를 원래 굴렸던 위치로 되돌려 보냅니다.
    void ReleaseFromKeepSlot(Dice d)
    {
        if (d.currentKeepIndex != -1)
        {
            keepSlotOccupants[d.currentKeepIndex] = null; d.currentKeepIndex = -1;
            d.MoveToTarget(d.rollPos);
        }
    }

    //기존 주사위를 파괴하고, 정해진 슬롯에 새로운 주사위들을 랜덤 값으로 생성합니다.
    void SpawnDice()
    {
        foreach (var d in activeDiceList) if (d != null) Destroy(d.gameObject);
        activeDiceList.Clear();
        System.Array.Clear(keepSlotOccupants, 0, keepSlotOccupants.Length);

        for (int i = 0; i < rollSlots.Length; i++)
        {
            GameObject go = Instantiate(dicePrefab, rollSlots[i].position, Quaternion.identity);
            Dice d = go.GetComponent<Dice>(); d.rollPos = rollSlots[i].position;
            d.SetValue(UnityEngine.Random.Range(1, 7)); activeDiceList.Add(d);
        }
    }
}