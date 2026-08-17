# DanbooruTagGen

danbooru 태그 조합으로 **와일드카드(랜덤 프롬프트) 줄**을 생성하는 .NET 9 WPF 앱.
"레시피"를 슬롯으로 구성하면, Stable Diffusion 계열에 바로 넣을 수 있는 프롬프트 줄을
매번 다르게 N개 뽑아준다.

## 구조

```
src/DanbooruTagGen.Core/   순수 로직 (WPF 의존 없음, 테스트 대상)
  Models/                  Recipe, Slot(다형성), Pool, GenerationOptions
  Generation/              WildcardGenerator, ConflictRules, TagOrdering, AnimaPhraseBook
  Persistence/             JsonStore, PoolStore, RecipeStore, PresetSeeder
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

`alternative`의 그룹에는 `weight`를 줄 수 있다(0이면 절대 안 뽑힘).

## 출력 형식

- **Tags** (기본) — 태그를 쉼표로 나열. Illustrious 계열용.
- **Anima** — Anima는 CLIP이 아니라 Qwen 계열 LLM을 텍스트 인코더로 써서 자연어를 잘 읽는
  대신 태그 과밀에 민감하다. 이 모드는 구도·조명 같은 장식 축을 버리고, 남은 태그를
  `data/anima-phrases.csv`(태그→영어 서술 조각)로 문장으로 바꿔 뒤에 덧붙인다.

## 빠른 시작

```
dotnet test
dotnet run --project src/DanbooruTagGen.App
```

## 데이터에 관하여

이 저장소에는 **태그 사전(`data/*.csv`)만** 들어 있다.
레시피·공용 풀·모순 규칙·서술 조각 사전과 제작 문서는 로컬에만 두고 git에 올리지 않는다
(`.gitignore` 참고).

해당 파일이 없어도 앱은 정상 기동한다 — 번들 프리셋이 비어 있고, 모순 검사와 Anima
서술문이 비활성화될 뿐이다. 레시피와 풀은 앱 안에서 직접 만들 수 있고,
`%APPDATA%\DanbooruTagGen\`에 저장된다.
