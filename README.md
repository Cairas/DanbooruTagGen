# DanbooruTagGen

danbooru 태그 조합으로 **와일드카드(랜덤 프롬프트) 줄**을 생성하는 .NET 9 WPF 앱.
"레시피"를 슬롯으로 구성하면, Stable Diffusion 계열에 바로 넣을 수 있는 프롬프트 줄을
매번 다르게 N개 뽑아준다.

## 구조

```
src/DanbooruTagGen.Core/   순수 로직 (WPF 의존 없음, 테스트 대상)
  Models/                  Recipe, Slot(다형성), Pool, GenerationOptions
  Generation/              WildcardGenerator, TagKey, ConflictRules, RequiresRules,
                           RecipeValidator, HookApplier, TagOrdering, VarietyAnalyzer
  Persistence/             JsonStore, PoolStore, RecipeLibraryStore, PresetSeeder
  Tags/                    TagDatabase, CsvTagParser
  Output/                  WildcardWriter

src/DanbooruTagGen.App/    WPF UI (MVVM, CommunityToolkit.Mvvm)
tests/                     xUnit — Core만 테스트

data/*.csv                 태그 사전 (danbooru 원본 + 한국어 별칭·카테고리)
```

## 핵심 모델 — Slot 다형성

레시피는 `Slots` 목록이고, 각 슬롯은 JSON `"type"` 필드로 셋 중 하나다:

| type | 동작 |
|---|---|
| `fixed` | 안의 태그가 **항상 전부** 나간다. 레시피의 정체성. |
| `randomPool` | 인라인 후보 + 공용 풀의 후보를 합쳐 매 줄 `Min~Max`개를 비복원 추출. |
| `alternative` | 그룹 중 **정확히 하나를 통째로** 뽑는다. 서로 섞이면 안 되는 상태 묶음에 쓴다. |

`alternative`의 그룹에는 `weight`를 줄 수 있다(0이면 절대 안 뽑힘). 랜덤 풀 후보 앞의
`N::`는 후보별 추첨 배율, `implies:태그`는 출력되지 않고 판정(모순 검사)에만 쓰이는 표식이다.

## 출력

태그를 쉼표로 나열한 한 줄씩 쓴다(`_`→공백 변환, 태그명 속 괄호는 `\(` `\)`로 이스케이프).
생성 직전 훅(사용자 정의 슬롯)이 복사본 레시피에 끼워지고, 모순 규칙(`data/conflicts.csv`)에
걸리는 줄은 재추첨한다. 여러 레시피를 한 번에 뽑아 한 파일 또는 레시피별 파일로 쓸 수 있다.

## 빠른 시작

```
dotnet test
dotnet run --project src/DanbooruTagGen.App
```

## 데이터에 관하여

이 저장소에는 **태그 사전(`data/*.csv`)만** 들어 있다.
레시피·공용 풀·모순 규칙과 제작 문서는 로컬에만 두고 git에 올리지 않는다
(`.gitignore` 참고).

해당 파일이 없어도 앱은 정상 기동한다 — 번들 프리셋이 비어 있고 모순 검사가 비활성화될
뿐이다. 레시피와 풀은 앱 안에서 직접 만들 수 있고, `%APPDATA%\DanbooruTagGen\`에 저장된다.

번들 프리셋(`data/presets`)은 처음 한 번만 사용자 데이터로 시딩되고(`settings.json`의
seededIds), 앱이 켜져 있는 동안 `data/presets/*.json`·`conflicts.csv`·`requires.csv` 변경은
파일 감시로 자동 반영된다.
