![UV Wizard](https://github.com/user-attachments/assets/bc80eb4c-c7ac-4625-b1cc-811c92079ef1)

Welcome to the **UVWizard** project! This add-on for Unity aims to simplify the process of combining multiple materials and textures on a 3D model into a single material with a unified texture atlas. This documentation will guide you through how the project works, its current status, future plans, and how you can contribute.

(This project was made during the 2024 [Hack K-State Hackathon](https://hackkstate.tech/))

---

## **Table of Contents**

1. [Introduction](#introduction)
2. [Understanding UV Mapping and Project Motivation](#understanding-uv-mapping-and-project-motivation)
   - [What are UVs and How Do They Work?](#what-are-uvs-and-how-do-they-work)
   - [Importance of Efficient UV Packing](#importance-of-efficient-uv-packing)
   - [Project Motivation](#project-motivation)
   - [Use Case in VRChat](#use-case-in-vrchat)
3. [How It Works](#how-it-works)
4. [Roadmap](#roadmap)
   - [Planned Features](#planned-features)
   - [Milestones](#milestones)
5. [Current Status](#current-status)
6. [Documentation](#documentation)
   - [Software Requirements](#software-requirements)
   - [Installation Instructions](#installation-instructions)
   - [Usage Instructions](#usage-instructions)
7. [Contributing](#contributing)
   - [Reporting Issues](#reporting-issues)
   - [Submitting Pull Requests](#submitting-pull-requests)
   - [Coding Guidelines](#coding-guidelines)
8. [License](#license)

---

## **Introduction**

**UVWizard** is a Unity add-on designed to optimize 3D models by:

- Combining multiple materials into a single material.
- Repacking UVs to correspond with a new texture atlas.
- Merging multiple textures into one texture atlas.

This tool is particularly useful for reducing draw calls and improving performance in games and applications that use complex models with multiple materials.

---

## **Understanding UV Mapping and Project Motivation**

### **What are UVs and How Do They Work?**

UV mapping is the process of projecting a 2D image texture onto a 3D model's surface. The letters "U" and "V" denote the axes of the 2D texture space, as "X," "Y," and "Z" are already used for 3D space. Each vertex in a 3D mesh is mapped to coordinates in the 2D texture, allowing the texture to wrap around the 3D geometry. 

Here is a great youtube video explaining everything you need to know to understand UV-mapping: [![UV Maps Explained](https://img.youtube.com/vi/Yx2JNbv8Kpg/0.jpg)](https://www.youtube.com/watch?v=Yx2JNbv8Kpg)


#### **Example**

   ![UV-mapping Unrapping](https://www.animum3d.com/wp-content/uploads/2022/02/UV-mapping-Unrapping-test.gif)
   *Figure: UV-mapping Unrapping animation example. [Source](https://www.animum3d.com/blog/el-uv-mapping-ese-gran-desconocido/)*


### **Importance of Efficient UV Packing**

Efficient UV packing involves arranging the UV islands (sections of the mesh that are mapped to the texture) as tightly as possible within the texture space. This maximizes the usage of the texture's resolution, resulting in higher-quality textures and better visual fidelity.

Poor UV packing can lead to wasted texture space, lower resolution for individual UV islands, and visible seams or artifacts on the model. By optimizing UV packing, we can:

- **Increase Texture Resolution**: Larger UV islands utilize more pixels, improving detail.
- **Reduce Texture Size**: Efficient packing allows for smaller textures without sacrificing quality.
- **Improve Performance**: Smaller textures consume less memory and bandwidth.

#### **Example**

   ![Unpacked vs. Packed UV Mapping](https://www.uv-packer.com/wp-content/uploads/2014/06/uv-packer_comparison_2.jpg)
   *Figure: Comparison between unpacked (left) and efficiently packed (right) UV maps. [Source](https://www.uv-packer.com/uv-packer-features-old/)*

### **Project Motivation**

Many creators, especially in communities like VRChat, design avatars and models without extensive knowledge of game optimization techniques. They may not be aware of the performance drawbacks associated with using multiple materials and meshes, which add additional Draw Calls and hurt performance. 
My aim with this project is to add a one-button solution to automatically optimize avatars without losing much, if any, quality.

I highly recommend watching this video to learn the heavy performance impact Draw Calls have on a runtime: [![Unity Performance Tips: Draw Calls](https://img.youtube.com/vi/IrYPkSIvpIw/0.jpg)](https://www.youtube.com/watch?v=IrYPkSIvpIw)

Common issues include:

- **Multiple Materials**: Each material on a model can add a draw call, impacting rendering performance.
- **Multiple Meshes**: Each mesh on a model can add a draw call, impacting rendering performance.
- **Inefficient Texture Usage**: Textures not fully utilized due to poor UV packing waste valuable resources by over using multiple meshes and/or materials.

#### **Challenges with Existing Solutions**

Existing material combining tools often align textures to a simple grid layout when merging multiple materials. For example, combining four materials might divide the texture atlas into four equal quadrants, reducing each original texture's resolution to 25%.

This method leads to:

- **Loss of Texture Quality**: Significant reduction in detail due to smaller allocated texture space.
- **Inefficient UV Usage**: Not all UV islands may need equal space, leading to wasted areas.

#### **Example**

  ![Polytool material/mesh combining](https://github.com/user-attachments/assets/7d7bf92e-dcb0-4aa0-941f-094c0e125af9)
  *Figure: Example of existing material/mesh combining method. [Source](https://markcreator.gumroad.com/l/Polytool)*

### **UVWizard's Solution**

**UVWizard** addresses these issues by:

- **Automated Optimization**: Takes a model or avatar and optimizes it to use a single material without losing quality.
- **Advanced UV Packing**: Packs UV islands as tightly as possible to maintain high resolution for each part of the model.
- **Preserving Texture Quality**: By efficiently using the texture space, the original texture quality is maintained.

#### **Benefits**

- **Improved Performance**: Reduces draw calls and optimizes texture usage, leading to smoother experiences, especially on devices with limited resources.
- **Accessibility**: Makes it easier for non-developers to optimize their models without deep technical knowledge.
- **Enhanced Community Interaction**: More users can enjoy detailed avatars across different platforms, including standalone VR headsets like the Oculus Quest.

### **Use Case in VRChat**

In VRChat, many users create avatars that are not optimized for standalone VR headsets. Quest users often cannot see these avatars correctly, hindering community interaction.

By using **UVWizard**:

- **Creators** can easily optimize their avatars for all platforms.
- **Users** benefit from better performance and a richer visual experience.
- **Community** engagement is enhanced as more users can see and interact with detailed avatars.

Ultimately, UVWizard improves community connectivity, performance, ease of development, and reduces compute costs.

---

## **How It Works**

The UVWizard script performs the following steps:

1. **Mesh and Material Retrieval**: Accesses the mesh and materials from the selected GameObject.
2. **UV Repacking**: Adjusts the UV coordinates to fit within designated regions of a new texture atlas.
3. **Texture Atlas Creation**: Combines individual textures from each material into a single texture atlas.
4. **Material Creation**: Generates a new material that uses the combined texture atlas.
5. **Application**: Applies the new UVs and material back to the mesh, replacing the original materials.

By automating these steps, UVWizard streamlines the process of optimizing models for better performance.

---

## **Roadmap**

### **Planned Features**

- **Advanced UV Packing Algorithms**: Implement more efficient UV packing techniques to optimize texture space usage.
- **Support for Additional Texture Maps**: Extend functionality to include normal maps, specular maps, and other material properties.
- **Editor Extension**: Develop a custom Unity Editor window for a more user-friendly interface.
- **Runtime Support**: Optimize the script for runtime use, allowing dynamic models to be processed during gameplay.
- **Error Handling and Logging**: Improve feedback and error messages for better troubleshooting.

### **Milestones**

1. **v1.0**: Basic functionality with manual script execution.
2. **v1.1**: Editor window integration for easier use.
3. **v2.0**: Advanced UV packing and support for additional texture maps.
4. **v2.1**: Runtime optimization and enhanced error handling.

---

## **Current Status**

- **Version**: 1.0
- **Implemented Features**:
  - Basic UV repacking using a grid layout.
  - Texture atlas creation using Unity's built-in `PackTextures` method.
  - Generation of a new material with the combined texture.
- **Known Issues**:
  - Limited UV packing efficiency due to simplistic algorithm.
  - Textures must be readable; non-readable textures require manual adjustment.
  - Does not currently support additional texture maps (e.g., normals, metallic).

---

## **Documentation**

### **Software Requirements**

- **Unity Editor**: Version 2022.3.22f1 or higher.
- **VRChat Creator Companion (VCC)**: Version 2.1.0 or newer.
- **Platform**: Compatible with Windows, macOS, and Linux versions of Unity Editor.
- **Dependencies**: No external packages required.

### **Installation Instructions**

**Using VRChat Creator Companion (VCC)**

To install UVWizard into your Unity project using the VRChat Creator Companion (VCC) and community packages, follow these steps:

1. **Add the UVWizard Repository to VCC**

   - **Open the VCC and Go to Settings**:
     - Launch the **VRChat Creator Companion**.
     - Click on the **Settings** icon in the top-right corner.

   - **Verify VCC Version**:
     - Ensure you're using VCC version **2.1.0** or newer (the version is displayed in the top-right corner of the Settings page).
     - If you're not on the latest version, scroll down and click **"Check for Updates"**.

   - **Navigate to the Packages Tab**:
     - In the Settings page, click on the **"Packages"** tab.

   - **Add Community Repository**:
     - You can refer to the official VRChat documentation for detailed instructions: [Adding Community Repositories](https://vcc.docs.vrchat.com/guides/community-repositories/)
     - **Option 1**: Click this link to automatically add the repository:
       - [Add UVWizard to VCC](https://kingazaz.github.io/uv-wizard/)
     - **Option 2**: Manually add the repository:
       - Click the **"Add Repository"** button.
       - In the input field that appears, enter the URL of the UVWizard repository:
         ```
         https://kingazaz.github.io/uv-wizard/index.json
         ```
       - Click **"Add"**.

   - **Confirm Repository Addition**:
     - A popup will appear showing information about the repository and the packages it contains.
     - Review the information to ensure it's correct.
     - Click **"I Understand, Add Repository"** to confirm.

   - **Verify the Repository is Added**:
     - The repository will now appear in your list of installed repositories.
     - You should see **UVWizard** listed among the available packages.

2. **Add UVWizard Package to Your Project**

   - **Navigate to Your Project**:
     - In the VCC, go to the **"Projects"** tab.
     - Select the Unity project where you want to install UVWizard.

   - **Install the UVWizard Package**:
     - In your project's details page, scroll to the **"Packages"** section.
     - Find **UVWizard** in the list of available community packages.
     - Click the **"Add"** or **"Install"** button next to UVWizard.

   - **Wait for Installation to Complete**:
     - The VCC will download and install the UVWizard package into your Unity project.
     - Once completed, UVWizard will be available in your project's Packages folder.

3. **Open Your Project in Unity**

   - Launch **Unity Hub**.
   - Open your project, and you should see the UVWizard script and associated assets imported.

### **Usage Instructions**

1. **Prepare Your Model**:

   - Ensure your GameObject has a `MeshFilter` and a `MeshRenderer` component.
   - The model should have multiple materials applied.

2. **Attach the UVWizard Script**:

   - Select the GameObject in the Unity Editor.
   - Click on **"Add Component"** in the Inspector window.
   - Search for `UVWizard` and add it to the GameObject.

3. **Configure the Script** (Optional/Coming soon):

   - In the Inspector, you can adjust the `Texture Atlas Size` to your preferred resolution (default is 2048).

4. **Execute the Script**:

   - The script with execute automatically on Start

5. **Verify the Result**:

   - After running the script, the GameObject should now have a single material.
   - The mesh UVs will be updated to correspond with the new texture atlas.
   - Check the `Materials` and `Mesh Renderer` components to confirm the changes.

---

## **Contributing**

Contributions are welcome! Here's how you can help:

### **Reporting Issues**

- Use the [Issues](https://github.com/KingAzaz/uv-wizard/issues) tab to report bugs or suggest enhancements.
- Provide detailed information, including steps to reproduce the issue and any relevant screenshots.

### **Submitting Pull Requests**

1. **Fork the Repository**:

   - Click on the **"Fork"** button at the top of the repository page.

2. **Create a Feature Branch**:

   - In your forked repository, create a new branch for your feature or fix:

     ```bash
     git checkout -b feature/YourFeatureName
     ```

3. **Commit Your Changes**:

   - Make your code changes and commit with clear messages:

     ```bash
     git commit -m "Add feature XYZ"
     ```

4. **Push to Your Fork**:

   ```bash
   git push origin feature/YourFeatureName
   ```

5. **Open a Pull Request**:
   - Navigate to the original repository and click on "New Pull Request."
   - Select your feature branch and submit the pull request for review.

### **Coding Guidelines**

   - **Follow C# Coding Standards**: Use consistent naming conventions and code formatting.
   - **Comment Your Code**: Provide clear comments and XML documentation where appropriate.
   - **Test Your Changes**: Ensure that your code works as intended and does not break existing functionality.

---

## **License**

This project is licensed under the [MIT License](LICENSE). You are free to use, modify, and distribute this software, but please provide attribution to the original author.

---

Thank you for your interest in UV Wizard! Your contributions and feedback are invaluable. Together, we can improve this tool to help the Unity community optimize their 3D models more efficiently.

Special thanks to [Markcreator](https://github.com/Markcreator) for inspiration for this project, and the generous help he provided during this project! <3
