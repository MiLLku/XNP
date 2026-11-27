# XNP 생성 에셋 인계표 — 2026-11-21

이 파일은 생성된 PNG의 위치와 Unity 연결 순서만 정리한다. ScriptableObject, 프리팹, 씬에는 아무 것도 자동 할당하지 않았다.

## 폴더

- `Assets/01.Asset/AssetOff/Generated/Items/` — 아이템 아이콘 및 4x4 아틀라스
- `Assets/01.Asset/AssetOff/Generated/Enemies/` — 적 9종
- `Assets/01.Asset/AssetOff/Generated/Nature/` — 작물·식물·나무
- `Assets/01.Asset/AssetOff/Generated/Structures/` — 적 구조물·건설 메뉴 아틀라스
- `Assets/01.Asset/AssetOff/Generated/Tiles/` — 범용 사다리 타일
- `Assets/01.Asset/Texture/` — 셰이더 몸통 재질

## 빈 ItemData 35종 — 개별 PNG

`ItemData.itemIcon`에 같은 이름의 PNG를 할당한다.

`AcidSac`, `Berry`, `BronzeIngot`, `CopperIngot`, `CopperOre`, `Corpse`, `DisguiseFilm`, `ErosionFiber`, `ErosionGland`, `EscortHeart`, `GoldIngot`, `GoldOre`, `GrapplerSinew`, `HatchlingAbyssDevourer`, `HatchlingAcidCrawler`, `HatchlingFiberSpider`, `HatchlingGlandBeetle`, `HatchlingSlimeLarva`, `HatchlingSporeSac`, `HighGradeFuel`, `IronIngot`, `IronOre`, `NeedleAlloy`, `Obsidian`, `OminousEgg1`, `OminousEgg2`, `OminousEgg3`, `RefinedAlloy`, `Sedative`, `SilverIngot`, `SilverOre`, `SpawnerCore`, `SyntheticResin`, `TinOre`, `TitaniumOre`.

## 기존 임시 ItemData — 4x4 아틀라스

아래 PNG들은 `Sprite Mode: Multiple`, `Grid By Cell Count: 4 x 4`로 자른다. 표기는 `행, 열`이며 1부터 센다.

### `EquipmentIcons_A.png`

1행: BasicArmor, BasicClothes, BasicHat, BasicGloves  
2행: BasicShoes, WorkGloves, WorkBoots, WaterproofBoots  
3행: HeatGloves, HeatBoots, HeatSuit1, HeatSuit2  
4행: HeatSuit3, HeatSuit4, AcidGloves, ProtectiveGloves

### `EquipmentIcons_B.png`

1행: ProtectiveHelmet1, ProtectiveHelmet2, ProtectiveHelmet3, ProtectiveSuit1  
2행: ProtectiveSuit2, ProtectiveSuit3, BronzeArmor, CopperArmor  
3행: IronArmor, SilverArmor, WoodArmor, NeedleArmor  
4행: NeedleAlloyArmor, EtherionArmor, GlandSuit, SinewArmor

### `EquipmentIcons_C.png`

1행: Baton, StoneSpear, BronzeSword, CopperSword  
2행: IronSword, SilverSword, RefinedSword, NeedleSword  
3행: NeedleAlloySword, EtherionSword, SinewWhip, Crossbow  
4행: HuntingBow, CopperCrossbow, IronCrossbow, SilverCrossbow

### `EquipmentIcons_D.png`

1행: NeedleCrossbow, NeedleAlloyCrossbow, EtherionCrossbow, PrecisionCrossbow  
2행: AcidThrower, GlandRifle, GrapplingGrip, AntidoteSac  
3행: FirstAidKit, DisguiseCloak, PurificationSerum, PurifyGrenade  
4행: PurifyingAmpoule, PurifyingGland, SpawnerCoreLure, ReinforcedArmor

### `FoodCropIcons.png`

1행: BerrySapling, ChefErosionSimpleMeal, ChefSimpleMeal, ErosionFeed  
2행: ErosionFruit, ErosionPotato, ErosionPotatoSeed, ErosionRice  
3행: ErosionRiceSeed, ErosionSimpleMeal, PotatoSeed, Potato  
4행: RiceSeed, Rice, SimpleMeal, Berry

### `ResourceNatureIcons.png`

1행: AbyssFruit, Coal, CrudeOil, Crystal  
2행: Dirt, ErodedWater, ErosionCrystal, Ether  
3행: Etherion, RaidTrophy, Stone, Water  
4행: TreeSapling, Wood, Obsidian, CopperOre

## 지형 및 건설 바닥

이 재질들은 셰이더가 상단 캡과 끊긴 끝 돌출부를 별도로 그리므로, PNG 자체에는 테두리/캡을 굽지 않았다.

- `Texture_CopperFloor.png` — CopperFloor(3009)
- `Texture_SilverFloor.png` — SilverFloor(3010)
- `Texture_GoldFloor.png` — GoldFloor(3011)
- `Texture_ProcessedDirt.png` — Tile_ProcessedDirt의 새 blendTexture 후보
- `Texture_AbyssFruit.png` — Tile_AbyssFruit의 새 blendTexture 후보
- `Generated/Tiles/LadderTile.png` — Tile_Ladder tileAsset 후보, 세로 반복형 투명 스프라이트

`Generated/Structures/FloorBuildIcons.png`은 건설 메뉴/청사진용 아틀라스다. 역시 4x4로 자른다.

1행: WoodFloor, StoneFloor, MetalFloor, DirtTile  
2행: Catwalk, Bridge, CopperFloor, SilverFloor  
3행: GoldFloor, WoodLadder, MetalLadder, WoodDoorData  
4행: PowerWire, BasicBed, Forge, ResearchWorkbench

다른 건설 메뉴 아이콘은 이미 있는 `Assets/01.Asset/AssetOff/<BuildingData 이름>.png`를 그대로 아이콘으로 쓸 수 있다. 예외로 `PowerGenerator`는 `WindGenerator.png`, `WoodDoorData`는 `WoodDoor.png`를 쓸 수 있다. `SawMill(101)`은 프리팹이 없는 구 데이터이므로 아이콘을 만들지 않았다.

## 월드 스프라이트

`Generated/Nature/`: PotatoCrop, RiceCrop, ErosionPotatoCrop, ErosionRiceCrop, ErosionBerryBush, ToxicFern, ContaminatedMushroom, Tree.

`Generated/Enemies/`: ErosionShooter, Grappler, Spitter, Bomber, DeepCrusher, Disguise, ErodedHumanMelee, ErodedHumanRanged, Escort.

`Generated/Structures/`: ErosionTower, FearTotem, Spawner, ErosionShooterProjectile.

