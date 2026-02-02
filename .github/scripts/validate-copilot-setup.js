#!/usr/bin/env node

/**
 * Validation script for GitHub Copilot configuration
 * This script validates that the repository follows best practices for Copilot coding agent
 * Reference: https://gh.io/copilot-coding-agent-tips
 */

const fs = require('fs');
const path = require('path');

const errors = [];
const warnings = [];
const info = [];

/**
 * Check if a file exists
 */
function fileExists(filePath) {
    return fs.existsSync(filePath);
}

/**
 * Read file content
 */
function readFile(filePath) {
    try {
        return fs.readFileSync(filePath, 'utf8');
    } catch (err) {
        return null;
    }
}

/**
 * Parse YAML frontmatter from markdown
 */
function parseFrontmatter(content) {
    const frontmatterMatch = content.match(/^---\n([\s\S]*?)\n---/);
    if (!frontmatterMatch) return null;
    
    const frontmatter = {};
    const lines = frontmatterMatch[1].split('\n');
    
    for (const line of lines) {
        const match = line.match(/^(\w+):\s*(.+)$/);
        if (match) {
            const [, key, value] = match;
            frontmatter[key] = value.replace(/^['"]|['"]$/g, '');
        }
    }
    
    return frontmatter;
}

/**
 * Validate main copilot instructions file
 */
function validateMainInstructions() {
    const filePath = '.github/copilot-instructions.md';
    
    if (!fileExists(filePath)) {
        errors.push('Missing .github/copilot-instructions.md file');
        return;
    }
    
    const content = readFile(filePath);
    if (!content || content.trim().length === 0) {
        errors.push('.github/copilot-instructions.md is empty');
        return;
    }
    
    info.push(`✓ Main instructions file exists (${content.length} bytes)`);
}

/**
 * Validate instruction files in .github/instructions/
 */
function validateInstructionFiles() {
    const instructionsDir = '.github/instructions';
    
    if (!fileExists(instructionsDir)) {
        errors.push('Missing .github/instructions/ directory');
        return;
    }
    
    const files = fs.readdirSync(instructionsDir).filter(f => f.endsWith('.md'));
    
    if (files.length === 0) {
        warnings.push('No instruction files found in .github/instructions/');
        return;
    }
    
    info.push(`✓ Found ${files.length} instruction files`);
    
    let filesWithApplyTo = 0;
    let filesWithDescription = 0;
    
    for (const file of files) {
        const content = readFile(path.join(instructionsDir, file));
        if (!content) continue;
        
        const frontmatter = parseFrontmatter(content);
        if (frontmatter) {
            if (frontmatter.applyTo) filesWithApplyTo++;
            if (frontmatter.description) filesWithDescription++;
        } else {
            warnings.push(`${file}: Missing frontmatter with applyTo and description`);
        }
    }
    
    info.push(`  - ${filesWithApplyTo} files with applyTo patterns`);
    info.push(`  - ${filesWithDescription} files with descriptions`);
}

/**
 * Validate agent files in .github/agents/
 */
function validateAgentFiles() {
    const agentsDir = '.github/agents';
    
    if (!fileExists(agentsDir)) {
        info.push('ℹ Optional: No .github/agents/ directory (agents are optional)');
        return;
    }
    
    const files = fs.readdirSync(agentsDir).filter(f => f.endsWith('.agent.md') || f.endsWith('.md'));
    
    if (files.length === 0) {
        info.push('ℹ No agent files found in .github/agents/ (agents are optional)');
        return;
    }
    
    info.push(`✓ Found ${files.length} agent files`);
    
    let agentsWithFrontmatter = 0;
    
    for (const file of files) {
        const content = readFile(path.join(agentsDir, file));
        if (!content) continue;
        
        const frontmatter = parseFrontmatter(content);
        if (frontmatter && frontmatter.description) {
            agentsWithFrontmatter++;
        }
    }
    
    info.push(`  - ${agentsWithFrontmatter} agents with proper frontmatter`);
}

/**
 * Validate prompt files in .github/prompts/
 */
function validatePromptFiles() {
    const promptsDir = '.github/prompts';
    
    if (!fileExists(promptsDir)) {
        info.push('ℹ Optional: No .github/prompts/ directory (prompts are optional)');
        return;
    }
    
    const files = fs.readdirSync(promptsDir).filter(f => f.endsWith('.prompt.md') || f.endsWith('.md'));
    
    if (files.length === 0) {
        info.push('ℹ No prompt files found in .github/prompts/ (prompts are optional)');
        return;
    }
    
    info.push(`✓ Found ${files.length} prompt files`);
}

/**
 * Validate collection files in .github/collections/
 */
function validateCollectionFiles() {
    const collectionsDir = '.github/collections';
    
    if (!fileExists(collectionsDir)) {
        info.push('ℹ Optional: No .github/collections/ directory (collections are optional)');
        return;
    }
    
    const files = fs.readdirSync(collectionsDir).filter(f => 
        f.endsWith('.collection.yml') || f.endsWith('.md')
    );
    
    if (files.length === 0) {
        info.push('ℹ No collection files found in .github/collections/ (collections are optional)');
        return;
    }
    
    info.push(`✓ Found ${files.length} collection files`);
}

/**
 * Main validation function
 */
function main() {
    console.log('🔍 Validating GitHub Copilot setup...\n');
    
    // Change to repository root
    process.chdir(path.join(__dirname, '../..'));
    
    validateMainInstructions();
    validateInstructionFiles();
    validateAgentFiles();
    validatePromptFiles();
    validateCollectionFiles();
    
    console.log('\n📊 Validation Results:\n');
    
    if (info.length > 0) {
        console.log('ℹ️  Information:');
        info.forEach(msg => console.log(`  ${msg}`));
        console.log('');
    }
    
    if (warnings.length > 0) {
        console.log('⚠️  Warnings:');
        warnings.forEach(msg => console.log(`  ${msg}`));
        console.log('');
    }
    
    if (errors.length > 0) {
        console.log('❌ Errors:');
        errors.forEach(msg => console.log(`  ${msg}`));
        console.log('');
        console.log('❌ Validation failed!\n');
        process.exit(1);
    }
    
    console.log('✅ Copilot setup validation passed!\n');
    console.log('Your repository is configured according to GitHub Copilot best practices.');
    console.log('Reference: https://gh.io/copilot-coding-agent-tips\n');
}

main();
