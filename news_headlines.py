import sys, os
import json
import requests as req
import splunk.entity as entity
import logging
import logging.handlers


def setup_logger(level):
    logger = logging.getLogger('api-data-fetch')
    logger.propagate = False  # Prevent the log messages from being duplicated in the python.log file
    logger.setLevel(level)
    file_handler = logging.handlers.RotatingFileHandler(os.environ['SPLUNK_HOME'] + '/var/log/splunk/top_headlines.log',
                                                        maxBytes=25000000, backupCount=5)
    formatter = logging.Formatter('%(asctime)s %(levelname)s %(message)s')
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)
    return logger


logger = setup_logger(logging.INFO)


def get_credentials(sessionKey):
    myapp = 'api-data-fetch'
    logger.info("Fetching credential for " + myapp)
    try:
        entities = entity.getEntities(['admin', 'passwords'], namespace=myapp, owner='nobody', sessionKey=sessionKey)
        # logger.info(entities)
    except Exception as e:
        raise Exception("Could not get %s credentials from splunk. Error: %s" % (myapp, str(e)))
    for i, c in entities.items():
        logger.info(c['username'])
        if c.get('api_uri') != "":
            logger.info(c['clear_password'])
            return c.get('api_uri'), c['username'], c['clear_password']
        raise Exception("No credentials have been found")


def news_api_call(requestURL, parameters):
    response = req.get(url=requestURL, params=parameters)
    if response.status_code != 200:
        print('Status: ', response.status_code, 'Headers: ', response.headers, 'Error Response: ', response.json())
        exit()
    data = response.json()
    return json.dumps(data)


def get_top_headline_by_page(requestURL, api_key, page_number=1):
    parameters = {"country": "in", "apiKey": api_key, "page": page_number}
    return news_api_call(requestURL, parameters)


def get_top_headline_for_day(requestURL, api_key):
    results_fetched = True
    page_number = 1
    final_list = []
    while results_fetched:
        top_headline_data = get_top_headline_by_page(requestURL, api_key, page_number)
        data = json.loads(top_headline_data)
        if len(data["articles"]) > 0:
            final_list.extend(data["articles"])
        else :
            break
        page_number += 1
    return final_list

def get_time_of_latest_fetched_headline(checkpoint_file):
    with open(checkpoint_file, "r") as file:
        first_line = file.readline()
        return first_line

def update_time_of_latest_fetched_headline(checkpoint_file, data):
    with open(checkpoint_file, 'w') as file:
        file.write(data)

def main():
    sessionKey = sys.stdin.readline().strip()
    logger.info("sessionkey : " + sessionKey)
    if len(sessionKey) == 0:
        logger.error("Did not receive a session key from splunkd.Please enable passAuth in inputs.conf")
        exit(2)
    api_url, userName, api_key = get_credentials(sessionKey)
    checkpoint_file = os.path.join(os.environ["SPLUNK_HOME"], 'etc', 'apps', 'api-data', 'bin', 'checkpoint',
                                   'checkpoint.txt')
    lastRunTime = get_time_of_latest_fetched_headline(checkpoint_file)
    top_headline_list = get_top_headline_for_day(api_url, api_key)
    headLinesToSendToHEC = []
    for top_headline in top_headline_list:
        if top_headline["publishedAt"] == lastRunTime :
            break
        headLinesToSendToHEC.append(top_headline)
    update_time_of_latest_fetched_headline(checkpoint_file, top_headline_list[0]["publishedAt"])

if __name__ == "__main__":
    main()