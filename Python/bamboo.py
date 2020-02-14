#!/usr/bin/env python
# -*- coding: utf-8 -*-

# Written in Python 3.7.4

import json
import requests

config = {}
config['data_dir'] = 'data'
config['base_url'] = 'https://bamboo.FOOBAR.com/rest/api/latest'
config['headers'] = {
    'Authorization': 'Basic [base64 encoded username:password]',
    'Content-Type': "application/json",
    'Accept': "*/*",
    'Cache-Control': "no-cache",
    'Accept-Encoding': "gzip, deflate",
    'Connection': "keep-alive"
    }
config['foo'] = {
    'plan_key': 'baz',
    'artifact_name': 'BAZ-Thing',
    'target_file': 'baz.exe',
    'final_name': 'foobar.exe'
}


def get_last_successful_build_url(plan_key):
    """
    Get the url of the most recent successful build in the plan
    param plan_key: Bamboo plan key
    returns: url if one found, None otherwise
    """
    url = '{}/result/{}.json'.format(config['base_url'], plan_key)
    qs = 'buildstate=Successful&max-results=1'
    last_success = requests.get(url, headers=config['headers'], params=qs)
    body = json.loads(last_success.text)

    try:
        return body['results']['result'][0]['link']['href']
    except KeyError:
        return None


def stage_artifact(url, plan_info):
    """
    Downloads the desired artifact from build
    param url: url of the build with (hopefully!) the artifact
    param plan_info: configuration information associated with the build plan
    """
    build_url = '{}.json'.format(url)
    qs = 'expand=artifacts'
    response = requests.get(build_url, headers=config['headers'], params=qs)
    body = json.loads(response.text)

    target_rpm = download_artifact(body['artifacts']['artifact'], plan_info['artifact_name'])
    if target_rpm is None:
        print('FATAL: Could not download {} from Bamboo url {}'.format(plan_info['artifact_name'], url))
        return

	# if lucky, use RPM library to extract item, otherwise, use shell commands or stand-alone tools to unpack
	

def download_artifact(artifact_list, artifact_name):
    """
    Search artifacts for the target, download if found, overwriting previous artifact
    param artifact_list: JSON blob describing available build artifacts
    param artifact_name: the name of the artifact to download
    returns: path to downloaded file if download successful, None otherwise
    """
    for a in artifact_list:
        if a['name'] == artifact_name:
            download_url = a['link']['href']
            dl_name = os.path.join(config['dl_dir'], 'foo.rpm')
            if os.path.isfile(dl_name):
                print('INFO: {} already downloaded, will not download again'.format(dl_name))
                return dl_name

            r = requests.get(download_url, headers=config['headers'])
            if r.status_code != 200:
                print('FATAL: Download attempt failed for {}'.format(download_url))
                print('FATAL: Received HTTP {} from server'.format(r.status_code))
                return None
      
            # for large items, download in chunks
            with open(dl_name, 'wb') as f:
                f.write(r.content)

            return dl_name

    return None
	

def get_foo_binary():
    """
    Does all the things needed to get the most recent FOO binary from Bamboo
    """
    build_url = get_last_successful_build_url(config['foo']['plan_key'])
    if build_url is None:
        print('FATAL: Could not download thing from Bamboo')
        print('FATAL: Unable to find successful build for plan {}'.format(config['foo']['plan_key']))
        return

    stage_artifact(build_url, config['foo'])
