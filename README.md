Thanks to [restoremonarchy](https://github.com/RestoreMonarchy) and [pterodactyl](http://github.com/pterodactyl) for the resources to make this!

# Rust Git Pterodactyl Egg Setup

This README guide will walk you through the process of setting up the Rust Git Pterodactyl egg with your own GitHub repository.

## Overview

This egg provides an Rust dedicated server with GitHub repository sync capabilities. It allows you to manage multiple Rust servers from a single private GitHub repository.

## Prerequisites

- A GitHub account
- Access to a Pterodactyl panel
- Basic knowledge of Git and GitHub

## Setup Steps

### 1. Create Your GitHub Repository

1. Log in to your GitHub account
2. Create a new private repository for your Rust server files
3. You don't need to initialize it with a README file

### 2. Structure Your Repository

1. Clone your new repository to your local machine
2. Create directories for each of your Rust servers, for example:
   ```
   /RustyRev1
   /RustyRev2
   /RustyRev2
   ```
3. Add your Rust Oxide server files to each directory
4. Commit and push the changes to GitHub

### 3. Get Repository URL

Your repository URL will look like this:
```
github.com/soerr/github-to-rust.git
```

### 4. Create a Personal Access Token

1. Go to GitHub Settings > Developer settings > Personal access tokens
2. Click "Generate new token"
3. Give it a descriptive name
4. Select only the `repo` scope to limit access to repository operations
5. Generate the token and copy it (you won't be able to see it again)

For more detailed instructions, refer to the [GitHub documentation on creating a personal access token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token).

### 5. Import the Egg to Pterodactyl

1. Download the `egg-Rust-git.json` file from this repository
2. In your Pterodactyl panel, navigate to the Nests section
3. Select the appropriate nest and click "Import Egg"
4. Upload the `egg-Rust-git.json` file

### 6. Create a Server

1. In the Pterodactyl panel, create a new server using this egg
2. Configure the server variables:
   - Set `REPOSITORY_ENABLED` to 1
   - Set `REPOSITORY_URL` to your GitHub repository URL
   - Set `REPOSITORY_ACCESS_TOKEN` to your personal access token
   - Set `REPOSITORY_BRANCH` to the desired branch (usually "main" or "master")
   - Set `REPOSITORY_DIR` to the specific server directory (e.g., "RustyRev1")
3. Configure other server settings as needed
4. Create and start the server

## Usage

- The server will automatically sync with your GitHub repository on startup
- Any changes pushed to the specified branch and directory will be pulled on the next server restart
- To update a server, push changes to its directory in your GitHub repository, then restart the server in Pterodactyl

## Managing Multiple Servers

- Create a new server in Pterodactyl for each Rust server
- Use the same repository URL and access token for all servers
- Set a unique `REPOSITORY_DIR` for each server corresponding to its directory in your GitHub repository

## Troubleshooting

- If the server fails to start, check the console logs for any error messages
- Ensure your GitHub repository is accessible and the access token is correct
- Verify that the `REPOSITORY_DIR` is set correctly for each server

## Extra Information

- You will need to upload both the plugin and the configuration or en file for it to sync properly. (The GitHub repository cannot retrieve files from the servers.)
- The egg-config.json file will delete the file directories and they will be replaced with the files from your GitHub repository. 
### So make sure you make a backup!
