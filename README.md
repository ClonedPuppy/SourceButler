# SourceButler

SourceButler is a Windows .NET 8 application designed to help you process source code files within a directory structure for usage in Large Language Models (LLMs) like ChatGPT, Claude, and others. It allows you to select specific folders and file extensions, scan and process files, and output the collected data into a structured text file suitable for LLM consumption.

![image](https://github.com/user-attachments/assets/eb63b4f6-8a31-4157-a967-898a1a84e2ab)


## Table of Contents

- [Features](#features)
- [Usage](#usage)
  - [Selecting a Root Directory](#selecting-a-root-directory)
  - [Selecting Folders and File Extensions](#selecting-folders-and-file-extensions)
  - [Processing Files](#processing-files)
- [Configuration](#configuration)
- [Dependencies](#dependencies)
- [Todo](#todo)

## Features

- **Directory Scanning**: Recursively scan directories while excluding specified folders (e.g., `.git`, `node_modules`).
- **Folder Selection**: Display a tree view of folders so you can select which ones to include in processing.
- **File Extension Filtering**: Detect and list file extensions present in the selected folders, allowing you to choose which extensions to include.
- **File Processing**: Read and aggregate the content of selected files, skipping large or binary files.
- **Output Generation**: Generate a structured text file containing the folder structure and file contents.
- **Configuration Persistence**: Save and load user configurations to a `.sourceButler.yml` file in the root directory.

## Usage

### Selecting a Root Directory

1. **Launch SourceButler**.
2. **Select Folder**:

   - Click the **Select Folder** button.
   - Choose the root directory you want to scan.

### Selecting Folders and File Extensions

1. **Folder Tree**:

   - A tree view of the folders will appear.
   - Check the boxes next to the folders you wish to include.

2. **File Extensions**:

   - On the right, a list of file extensions found in the selected folders will be displayed.
   - Check the boxes next to the file extensions you want to process.

### Processing Files

1. **Start Processing**:

   - Click the **Process Files** button.
   - The application will scan and process the selected files.

2. **Output File**:

   - After processing, a save dialog will appear.
   - Choose the location to save the output text file.

## Configuration

- **Configuration File**:

  - A `.sourceButler.yml` file is created in the root directory.
  - This file saves your selected folders and file extensions.

- **Settings Persistence**:

  - On subsequent uses, SourceButler will load your previous configuration.
  - If the root directory changes, the configuration will reset.

## Dependencies

- **.NET 8.0**: Target framework for the application.
- **YamlDotNet (Version 16.2.0)**: Used for reading and writing YAML configuration files.

  ```xml
  <PackageReference Include="YamlDotNet" Version="16.2.0" />
  ```

## Todo

- **Async fixes**: Need to sort this out later, app can get a little sluggish if working with huge folder hierarchies.
- **bugs**: I'm sure there are plenty...
