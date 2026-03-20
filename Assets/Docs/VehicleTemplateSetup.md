# Vehicle Setup Guide (Manual, from scratch)
This guide describes how to add a new car (example: Mercedes) so it appears in Main Menu and works in game exactly like existing cars.
## 1) Create folder structure
Create:
- Assets/CarData/Vehicles/<YourCar>/Prefabs/Body
- Assets/CarData/Vehicles/<YourCar>/Prefabs/Wheels
- Assets/CarData/Vehicles/<YourCar>/ScriptableObjects/Config
- Assets/CarData/Vehicles/<YourCar>/ScriptableObjects/Tuning
- Assets/CarData/Vehicles/<YourCar>/ScriptableObjects/Paints
- Assets/CarData/Vehicles/<YourCar>/ScriptableObjects/BodySets
Fastest template path: duplicate Assets/CarData/Vehicles/Buick_GNX and rename files/assets.
## 2) Prepare body prefabs
In Prefabs/Body prepare:
- Main body prefab (Car_<YourCar>_SetA.prefab recommended)
- Optional menu body prefab (Car_<YourCar>_Menu.prefab) if your pipeline uses separate menu visuals
- Body set prefabs (BodySetA.prefab, BodySetB.prefab, ...)
Expected hierarchy in main body prefab:
- Body
- Root (common meshes that are always present)
- Body sets are instantiated as siblings near Root at local zero transform.
For each body set prefab:
- localPosition = (0,0,0)
- localRotation = (0,0,0)
- localScale = (1,1,1)
## 3) Prepare wheel prefab
In Prefabs/Wheels create wheel visual prefab used by PlayerCarConfig.visual.wheelPrefab.
## 4) Create ScriptableObjects
Create these assets (or duplicate Buick ones and retarget):
### 4.1 PlayerCarConfig
Path example:
- ScriptableObjects/Config/<YourCar>.asset
Fill at minimum:
- isual.bodyPrefab -> your main body prefab
- isual.wheelPrefab -> your wheel prefab
- isual.generateConvexBodyColliders as needed
- wheelbase/axle/wheel height values for your model
- damage section (can start by copying Buick values)
### 4.2 BodySetConfig assets
Path example:
- ScriptableObjects/BodySets/<YourCar>_BodySet_SetA.asset
- ScriptableObjects/BodySets/<YourCar>_BodySet_SetB.asset
Set:
- odySetPrefab -> corresponding BodySetX.prefab
### 4.3 Tuning assets
Create/duplicate:
- VehicleHandlingConfig
- several EngineConfig
- several SuspensionConfig
### 4.4 Paint assets
Create multiple paint option assets for garage selection.
### 4.5 CarLoadoutConfig
Path example:
- ScriptableObjects/Tuning/<YourCar>_Loadout.asset
Set fields:
- displayName
- playerCarConfig
- handlingConfig
- odySets[]
- engineConfigs[]
- suspensionConfigs[]
- paintOptions[]
- defaults indexes
## 5) Connect to Main Menu
Open Assets/Scenes/MainMenu.unity.
Find GarageMenuController component and append your loadout into:
- carLoadouts
After this, car appears in menu carousel/list.
## 6) Validation checklist
1. Car appears in Main Menu selection.
2. Selecting car spawns correct body + wheels in preview.
3. Body set switch in garage replaces set prefab correctly.
4. Start game spawns selected car and selected body set.
5. Damage/deformation applies visually on chosen body.
## 7) What to retarget first after cloning Buick
If you duplicated Buick as a template, replace these references first:
- PlayerCarConfig.visual.bodyPrefab
- PlayerCarConfig.visual.wheelPrefab
- Every BodySetConfig.bodySetPrefab
- CarLoadoutConfig.displayName
Then tune handling/suspension values.
