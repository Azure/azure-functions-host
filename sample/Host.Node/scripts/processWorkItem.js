function processWorkItem(workItem, callback) {
    console.log('Node.js job function processed work item ' + workItem.ID);
    callback();
}