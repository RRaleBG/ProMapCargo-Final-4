/**
 * ComponentMapper: BM25-based component search for Stage 4
 * 
 * Maps user queries (element type + content hints) to Syncfusion .NET MAUI components
 * using BM25 ranking on component keywords.
 * 
 * Usage:
 *     const { ComponentMapper, stage_4_component_picking } = require('./controls_search.cjs');
 *     
 *     const componentMapping = {...}; // From Stage 3 (component-mapping.json)
 *     const result = stage_4_component_picking(componentMapping);
 *     // Returns: Stage 4 output JSON with mapped components
 * 
 *     // Or standalone CLI
 *     const mapper = new ComponentMapper('controls.csv');
 *     const results = mapper.search('button icon action', 5);
 *     // Returns: [(component_name, skill_name, score), ...]
 */

const fs = require('fs');
const path = require('path');
const { createReadStream } = require('fs');
const { parse } = require('csv-parse/sync');

/**
 * BM25 ranking algorithm for component keyword search.
 */
class BM25 {
  /**
   * Initialize BM25 with documents and parameters.
   * 
   * @param {string[]} documents - List of documents (keyword strings)
   * @param {number} k1 - Saturation parameter (default: 1.5)
   * @param {number} b - Length normalization parameter (default: 0.75)
   */
  constructor(documents, k1 = 1.5, b = 0.75) {
    this.k1 = k1;
    this.b = b;
    this.documents = documents;
    this.corpusSize = documents.length;
    
    // Tokenize and build index
    this.tokenizedDocs = documents.map(doc => this._tokenize(doc));
    this.idfCache = {};
    this.avgDocLength = this.tokenizedDocs.reduce((sum, doc) => sum + doc.length, 0) / Math.max(1, this.corpusSize);
    this._computeIdf();
  }
  
  /**
   * Simple tokenization: split on whitespace and commas.
   * 
   * @param {string} text - Text to tokenize
   * @returns {string[]} - Array of tokens
   */
  _tokenize(text) {
    return text
      .toLowerCase()
      .replace(/,/g, ' ')
      .split(/\s+/)
      .filter(token => token.trim().length > 0);
  }
  
  /**
   * Precompute IDF scores for all terms.
   */
  _computeIdf() {
    const termDocCount = new Map();
    
    for (const doc of this.tokenizedDocs) {
      const uniqueTerms = new Set(doc);
      for (const term of uniqueTerms) {
        termDocCount.set(term, (termDocCount.get(term) || 0) + 1);
      }
    }
    
    for (const [term, docCount] of termDocCount.entries()) {
      // BM25 IDF formula: log((N - n(t) + 0.5) / (n(t) + 0.5))
      const idf = Math.log((this.corpusSize - docCount + 0.5) / (docCount + 0.5) + 1.0);
      this.idfCache[term] = idf;
    }
  }
  
  /**
   * Score a document against a query using BM25.
   * 
   * @param {string} query - Query string
   * @param {number} docIndex - Index of document to score
   * @returns {number} - BM25 score (higher = better match)
   */
  score(query, docIndex) {
    const queryTerms = this._tokenize(query);
    const doc = this.tokenizedDocs[docIndex];
    const docLength = doc.length;
    
    let score = 0.0;
    for (const term of queryTerms) {
      const idf = this.idfCache[term] || 0.0;
      
      // Count term occurrences in document
      const termFreq = doc.filter(t => t === term).length;
      
      // BM25 formula
      const numerator = idf * termFreq * (this.k1 + 1);
      const denominator = termFreq + this.k1 * (1 - this.b + this.b * (docLength / this.avgDocLength));
      
      score += numerator / denominator;
    }
    
    return score;
  }
}

/**
 * Map user queries to Syncfusion .NET MAUI components using BM25 search.
 */
class ComponentMapper {
  /**
   * Initialize ComponentMapper by loading controls.csv.
   * 
   * @param {string} csvPath - Path to controls.csv file
   * @throws {Error} - If controls.csv does not exist or is invalid
   */
  constructor(csvPath = 'controls.csv') {
    this.csvPath = csvPath;
    
    if (!fs.existsSync(this.csvPath)) {
      throw new Error(`Components CSV not found: ${csvPath}`);
    }
    
    // Load CSV data
    this.components = [];
    this.keywordsList = [];
    
    try {
      const csvContent = fs.readFileSync(this.csvPath, 'utf-8');
      const records = parse(csvContent, {
        columns: true,
        skip_empty_lines: true
      });
      
      for (const row of records) {
        this.components.push({
          id: row['Component ID'],
          name: row['Component Name'],
          skill: row['Skill Name'],
          keywords: row['Keywords']
        });
        this.keywordsList.push(row['Keywords']);
      }
    } catch (error) {
      throw new Error(`Failed to load CSV: ${error.message}`);
    }
    
    if (this.components.length === 0) {
      throw new Error('No components found in CSV');
    }
    
    // Initialize BM25 with keywords
    this.bm25 = new BM25(this.keywordsList, 1.5, 0.75);
  }
  
  /**
   * Search for components matching the query.
   * 
   * @param {string} query - User query (e.g., "button primary action")
   * @param {number} topK - Number of top results to return (default: 5)
   * @returns {Array<[string, string, number]>} - List of [component_name, skill_name, score]
   */
  search(query, topK = 5) {
    if (!query || query.trim().length === 0) {
      return [];
    }
    
    // Score all documents
    const scores = [];
    for (let i = 0; i < this.components.length; i++) {
      const score = this.bm25.score(query, i);
      if (score > 0) {
        const component = this.components[i];
        scores.push([
          component.name,
          component.skill,
          score
        ]);
      }
    }
    
    // Sort by score descending
    scores.sort((a, b) => b[2] - a[2]);
    
    return scores.slice(0, topK);
  }
  
  /**
   * Get component skill by exact component name.
   * 
   * @param {string} componentName - Exact component name
   * @returns {Array<string>|null} - [component_name, skill_name] or null if not found
   */
  getByName(componentName) {
    for (const component of this.components) {
      if (component.name === componentName) {
        return [component.name, component.skill];
      }
    }
    return null;
  }
  
  /**
   * Get all components for a specific skill.
   * 
   * @param {string} skillName - Skill name (e.g., 'syncfusion-maui-buttons')
   * @returns {Array<[string, string]>} - List of [component_name, skill_name]
   */
  getBySkill(skillName) {
    return this.components
      .filter(c => c.skill === skillName)
      .map(c => [c.name, c.skill]);
  }
  
  /**
   * Get all unique skill names in the database.
   * 
   * @returns {string[]} - List of skill names
   */
  listSkills() {
    const skills = new Set(this.components.map(c => c.skill));
    return Array.from(skills).sort();
  }
  
  /**
   * Get all components.
   * 
   * @returns {Array<[string, string]>} - List of [component_name, skill_name]
   */
  listAll() {
    return this.components.map(c => [c.name, c.skill]);
  }
}

/**
 * Map user queries to Syncfusion EJ2 icons using BM25 search.
 */
class IconMapper {
  /**
   * Initialize IconMapper by loading icons.csv.
   * 
   * @param {string} csvPath - Path to icons.csv file
   * @throws {Error} - If icons.csv does not exist or is invalid
   */
  constructor(csvPath = 'icons.csv') {
    this.csvPath = csvPath;
    
    if (!fs.existsSync(this.csvPath)) {
      throw new Error(`Icons CSV not found: ${csvPath}`);
    }
    
    // Load CSV data
    this.icons = [];
    this.keywordsList = [];
    
    try {
      const csvContent = fs.readFileSync(this.csvPath, 'utf-8');
      const records = parse(csvContent, {
        columns: true,
        skip_empty_lines: true
      });
      
      for (const row of records) {
        this.icons.push({
          id: row['Icon ID'],
          name: row['Icon Name'],
          iconClass: row['Icon Class'],
          keywords: row['Keywords']
        });
        this.keywordsList.push(row['Keywords']);
      }
    } catch (error) {
      throw new Error(`Failed to load icons CSV: ${error.message}`);
    }
    
    if (this.icons.length === 0) {
      throw new Error('No icons found in CSV');
    }
    
    // Initialize BM25 with keywords
    this.bm25 = new BM25(this.keywordsList, 1.5, 0.75);
  }
  
  /**
   * Search for icons matching the query.
   * 
   * @param {string} query - User query (e.g., "save persist store")
   * @param {number} topK - Number of top results to return (default: 1)
   * @returns {Array<[string, string, number]>} - List of [icon_name, icon_class, score]
   */
  search(query, topK = 1) {
    if (!query || query.trim().length === 0) {
      return [];
    }
    
    // Score all documents
    const scores = [];
    for (let i = 0; i < this.icons.length; i++) {
      const score = this.bm25.score(query, i);
      if (score > 0) {
        const icon = this.icons[i];
        scores.push([
          icon.name,
          icon.iconClass,
          score
        ]);
      }
    }
    
    // Sort by score descending
    scores.sort((a, b) => b[2] - a[2]);
    
    return scores.slice(0, topK);
  }
  
  /**
   * Get icon class by exact icon name.
   * 
   * @param {string} iconName - Exact icon name
   * @returns {Array<string>|null} - [icon_name, icon_class] or null if not found
   */
  getByName(iconName) {
    for (const icon of this.icons) {
      if (icon.name === iconName) {
        return [icon.name, icon.iconClass];
      }
    }
    return null;
  }
  
  /**
   * Get all icons for a specific category.
   * 
   * @param {string} category - Category name (e.g., 'user_actions')
   * @returns {Array<[string, string]>} - List of [icon_name, icon_class]
   */
  getByCategory(category) {
    return this.icons
      .filter(i => i.category === category)
      .map(i => [i.name, i.iconClass]);
  }
  
  /**
   * Get all icons with names, classes, and keywords.
   * 
   * @returns {Array<[string, string, string]>} - List of [icon_name, icon_class, keywords]
   */
  listAll() {
    return this.icons.map(i => [i.name, i.iconClass, i.keywords]);
  }
}

/**
 * Stage 4: Process Stage 3 component-mapping.json and map elements to Syncfusion .NET MAUI components AND icons.
 * 
 * Supports both flat mappings (simple elements array) and complex mappings (sections with elements).
 * 
 * @param {Object} layoutJson - Stage 3 output JSON with one of two structures
 * @param {string} csvPath - Path to controls.csv
 * @param {string} iconsCsvPath - Path to icons.csv
 * @returns {Object} - Stage 4 output JSON with mapped components AND icons
 */
function stage4ComponentPicking(layoutJson, csvPath = 'controls.csv', iconsCsvPath = 'icons.csv') {
  const mapper = new ComponentMapper(csvPath);
  const iconMapper = new IconMapper(iconsCsvPath);
  const mappedComponents = [];
  const mappedIconElements = [];
  const mappedSections = [];
  
  // AUTO-FLATTEN: Handle both nested sections and flat elements
  let elementsToMap = [];
  const hasSections = layoutJson.sections && layoutJson.sections.length > 0;
  
  if (hasSections) {
    // COMPLEX MAPPING: Extract elements from sections
    for (const section of layoutJson.sections || []) {
      const sectionId = section.section_id || '';
      const sectionElements = [];
      
      for (const element of section.elements || []) {
        // Preserve section context
        element._section_id = sectionId;
        elementsToMap.push(element);
        sectionElements.push(element.id || '');
      }
      
      // Store section mapping for output
      if (sectionElements.length > 0) {
        mappedSections.push({
          section_id: sectionId,
          section_name: section.section_name || '',
          elements: sectionElements
        });
      }
    }
  } else {
    // SIMPLE MAPPING: Use flat elements array
    elementsToMap = layoutJson.elements || [];
  }
  
  // MAP ELEMENTS TO COMPONENTS AND ICONS
  for (const element of elementsToMap) {
    // Build search query from element metadata
    const query = `${element.type_hint || ''} ${element.description || ''}`.trim();
    
    // Search for best component (top-1 match)
    const results = mapper.search(query, 1);
    
    const componentMap = {
      element_id: element.id,
      element_name: element.name
    };
    
    // Include section_id if present (complex mappings)
    if (element._section_id) {
      componentMap.section_id = element._section_id;
    }
    
    if (results.length > 0) {
      // Found Syncfusion component
      const [componentName, skillName, score] = results[0];
      componentMap.component = componentName;
      componentMap.skill = skillName;
      componentMap.score = Math.round(score * 100) / 100;
    } else {
      // No Syncfusion match - use native MAUI
      componentMap.component = 'NATIVE_MAUI';
      componentMap.skill = null;
      componentMap.score = 0;
    }
    
    // MAP ICON if icon_hint provided
    if (element.icon_hint) {
      const iconQuery = element.icon_hint.trim();
      const iconResults = iconMapper.search(iconQuery, 1);
      
      if (iconResults.length > 0) {
        const [iconName, iconClass, iconScore] = iconResults[0];
        componentMap.icon = {
          name: iconName,
          iconCss: iconClass,
          score: Math.round(iconScore * 100) / 100
        };
      }
    }
    
    mappedComponents.push(componentMap);
  }
  
  // MAP ICON-ONLY ELEMENTS
  for (const iconElement of layoutJson.icon_elements || []) {
    const iconMap = {
      element_id: iconElement.id || '',
      element_name: iconElement.name || '',
      element_type: iconElement.type || 'icon'
    };
    
    // Map icon
    const iconQuery = (iconElement.icon_hint || '').trim();
    const iconResults = iconMapper.search(iconQuery, 1);
    
    if (iconResults.length > 0) {
      const [iconName, iconClass, iconScore] = iconResults[0];
      iconMap.icon = {
        name: iconName,
        iconCss: iconClass,
        score: Math.round(iconScore * 100) / 100
      };
    } else {
      iconMap.icon = {
        name: 'Unknown',
        iconCss: 'e-icons e-help',
        score: 0
      };
    }
    
    mappedIconElements.push(iconMap);
  }
  
  // BUILD OUTPUT
  const output = {
    component_type: layoutJson.component_type || 'Unknown',
    variant: layoutJson.variant || 'Default',
    mapped_components: mappedComponents
  };
  
  // Include icon-only elements if present
  if (mappedIconElements.length > 0) {
    output.mapped_icon_elements = mappedIconElements;
  }
  
  // Include section organization for complex mappings
  if (hasSections && mappedSections.length > 0) {
    output.mapped_sections = mappedSections;
  }
  
  return output;
}

/**
 * Demo: Search for MAUI components and print results.
 */
function demoSearch() {
  console.log('='.repeat(80));
  console.log('MAUI COMPONENT MAPPER - BM25 Search Demo');
  console.log('='.repeat(80));
  
  const mapper = new ComponentMapper();
  
  const testQueries = [
    'button icon action',
    'data grid table sorting filtering',
    'date picker calendar selection',
    'navigation drawer menu hamburger',
    'chart visualization dashboard',
    'data entry form validation',
    'notification badge alert',
    'modal dialog popup overlay'
  ];
  
  for (const query of testQueries) {
    console.log(`\nQuery: '${query}'`);
    console.log('-'.repeat(80));
    const results = mapper.search(query, 3);
    
    if (results.length > 0) {
      for (let i = 0; i < results.length; i++) {
        const [name, skill, score] = results[i];
        console.log(`  ${i + 1}. ${name.padEnd(30)} | ${skill.padEnd(35)} | Score: ${score.toFixed(4)}`);
      }
    } else {
      console.log('  No matches found.');
    }
  }
  
  console.log('\n' + '='.repeat(80));
  console.log(`Total components in database: ${mapper.components.length}`);
  console.log(`Total skills: ${mapper.listSkills().length}`);
  console.log('='.repeat(80));
}

// Main execution
if (require.main === module) {
  const args = process.argv.slice(2);
  
  if (args.length > 0) {
    // Stage 4 mode: Process component-mapping.json file
    const jsonFile = args[0];
    
    // Get the directory where this script is located
    const scriptDir = path.dirname(__filename);
    
    // Resolve path: try absolute first, then relative to script
    let jsonPath = jsonFile;
    if (!path.isAbsolute(jsonPath)) {
      jsonPath = path.join(scriptDir, jsonFile);
    }
    
    const componentsCsv = path.join(scriptDir, 'controls.csv');
    const iconsCsv = path.join(scriptDir, 'icons.csv');
    
    try {
      if (!fs.existsSync(jsonPath)) {
        throw new Error(`JSON file not found: ${jsonFile}\nTried: ${path.resolve(jsonPath)}`);
      }
      
      const layoutJson = JSON.parse(fs.readFileSync(jsonPath, 'utf-8'));
      const result = stage4ComponentPicking(layoutJson, componentsCsv, iconsCsv);
      console.log(JSON.stringify(result, null, 2));
    } catch (error) {
      console.error(`Error: ${error.message}`);
      process.exit(1);
    }
  } else {
    // Demo mode: Run test queries
    demoSearch();
  }
}

// Export functions and classes
module.exports = {
  BM25,
  ComponentMapper,
  IconMapper,
  stage4ComponentPicking,
  demoSearch
};
